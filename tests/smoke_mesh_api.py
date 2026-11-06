"""Exercise Phase 3 through device HTTP traffic against a dedicated running API."""
from datetime import datetime, timedelta, timezone
import json
import os
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = os.environ.get("SMARTX_API_URL", "http://localhost:5080/").rstrip("/")
checks = 0


def request(path, payload=None, expected=200, method=None):
    data = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(BASE + path, data=data, method=method,
        headers={"Content-Type": "application/json"})
    try:
        response = urllib.request.urlopen(req, timeout=8)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        body = response.read().decode()
        assert response.status == expected, (path, response.status, body)
        return json.loads(body) if body and body.lstrip().startswith(("{", "[")) else body


def check(condition, message):
    global checks
    assert condition, message
    checks += 1
    print("PASS:", message)


def until(predicate):
    deadline = time.monotonic() + 5
    while time.monotonic() < deadline:
        result = predicate()
        if result:
            return result
        time.sleep(0.05)
    raise AssertionError("Timed out waiting for actual HTTP telemetry processing")


def get(path, **query):
    return request(path + "?" + urllib.parse.urlencode(query))


def iso(value):
    return value.isoformat().replace("+00:00", "Z")


def link(source, target, cost, enabled=True):
    return request("/api/mesh/links", {"source": source, "target": target,
        "cost": cost, "enabled": enabled}, method="PUT")


def remove(source, target):
    return request("/api/mesh/links?" + urllib.parse.urlencode({"source": source, "target": target}),
        expected=204, method="DELETE")


def route(source, target="GATEWAY", algorithm="bfs"):
    return get("/api/mesh/route", **{"from": source, "to": target, "algorithm": algorithm})


request("/api/realtime/simulation/stop", {}, method="POST")
suffix = uuid.uuid4().hex[:8]
ids = ["mesh-" + name + "-" + suffix for name in ("A", "B", "C")]
macs = ["02:" + ":".join(f"{byte:02X}" for byte in bytes.fromhex(suffix) + bytes([index])) for index in range(3)]
for device_id, mac in zip(ids, macs):
    request("/api/sensors/register", {"deviceId": device_id, "macAddress": mac,
        "location": "Mesh HTTP fixture " + suffix, "category": "Temperature"}, 201)
snapshot = request("/api/mesh/topology")
check(snapshot["isSimulated"] and any(node["isGateway"] and node["isConnected"] for node in snapshot["nodes"]),
    "topology identifies simulated wireless links and the online gateway")
new_nodes = [node for node in snapshot["nodes"] if node["deviceId"] in ids]
check(len(new_nodes) == 3 and all(not node["isConnected"] and node["lastSeenUtc"] is None for node in new_nodes),
    "new mesh registrations do not fabricate telemetry or connectivity")
check(not route(ids[0])["found"], "offline device cannot claim an active route")


def send(device_id, value):
    return request("/api/telemetry/ingest", {"deviceId": device_id, "category": "Temperature",
        "valueType": "float", "value": str(value), "unit": "C"}, 202)


packets = [send(ids[0], value) for value in (21, 22, 23)]
send(ids[1], 24)
send(ids[2], 25)
until(lambda: all(node["isConnected"] for node in request("/api/mesh/topology")["nodes"] if node["deviceId"] in ids))
check(True, "real HTTP ingestion activates registered mesh nodes")

from_time = packets[0]["timestampUtc"]
to_time = packets[-1]["timestampUtc"]
history = get("/api/mesh/history", device=ids[0], fromUtc=from_time, toUtc=to_time)
check([row["packetId"] for row in history["readings"]] == [packet["packetId"] for packet in packets],
    "inclusive AVL range returns the actual ingested device packet identities")
check(history["retainedCount"] == 3 and 0 < history["treeHeight"] <= 2 and history["visitedNodes"] > 0 and not history["truncated"],
    "history exposes retained size, AVL height and query traversal diagnostics")
exact = get("/api/mesh/history", device=ids[0], fromUtc=from_time, toUtc=from_time)
check(exact["readings"][0]["packetId"] == packets[0]["packetId"] and
      all(row["timestampUtc"] == from_time for row in exact["readings"]),
    "exact timestamp boundaries include all and only matching observations")
alias_history = get("/api/mesh/history", device=macs[0].replace(":", "-"), fromUtc=from_time, toUtc=to_time)
check([row["packetId"] for row in alias_history["readings"]] == [packet["packetId"] for packet in packets],
    "history lookup resolves a normalized device MAC address")
limited = get("/api/mesh/history", device=ids[0], fromUtc=from_time, toUtc=to_time, limit=1)
check(len(limited["readings"]) == 1 and limited["truncated"], "range limit reports truncation explicitly")
old_start = iso(datetime.now(timezone.utc) - timedelta(days=365))
old_end = iso(datetime.now(timezone.utc) - timedelta(days=364))
check(not get("/api/mesh/history", device=ids[0], fromUtc=old_start, toUtc=old_end)["readings"],
    "history outside the retained observation period is empty")

# Isolate these three fixture nodes while preserving unrelated pre-existing links.
for edge in request("/api/mesh/topology")["links"]:
    if edge["sourceId"] in ids or edge["targetId"] in ids:
        remove(edge["sourceId"], edge["targetId"])
a, b, c = ids
link(a, "GATEWAY", 10)
link(a, b, 1)
link(b, "GATEWAY", 1)
link(b, c, 2)
link(c, "GATEWAY", 5)
bfs = route(a)
check(bfs["found"] and bfs["path"] == [a, "GATEWAY"] and bfs["hops"] == 1 and bfs["totalCost"] == 10,
    "BFS selects the fewest-hop route and reports its energy cost")
weighted = route(a, algorithm="dijkstra")
check(weighted["path"] == [a, b, "GATEWAY"] and weighted["totalCost"] == 2,
    "Dijkstra selects a cheaper multi-hop route")
check(route(macs[0].replace(":", "").lower(), macs[1].replace(":", "-"))["path"] == [a, b],
    "routing resolves compact and hyphenated MAC endpoints")
mst_route = route(c, algorithm="mst")
check(mst_route["found"] and mst_route["path"] == [c, b, "GATEWAY"] and mst_route["totalCost"] == 3,
    "MST routing follows the minimum-energy spanning backbone")
tree = request("/api/mesh/topology")["spanningTree"]
fixture_edges = [edge for edge in tree["links"] if edge["sourceId"] in ids or edge["targetId"] in ids]
check(len(fixture_edges) == 3 and sum(edge["cost"] for edge in fixture_edges) == 4,
    "Prim selects the fixture's known minimum total link energy")
link(a, "GATEWAY", 10, False)
check(route(a)["path"] == [a, b, "GATEWAY"], "disabled wireless link causes route fallback")
link(a, "GATEWAY", 10)
remove(a, "GATEWAY")
remove(a, b)
check(not route(a)["found"] and not route(a)["path"], "isolated online node returns an explicit unreachable route")
traversal = get("/api/mesh/traversal", **{"from": "GATEWAY"})
check(a in traversal["unreachableDeviceIds"] and b in traversal["reachableDeviceIds"] and c in traversal["reachableDeviceIds"],
    "BFS traversal classifies active reachable and isolated nodes")
link(a, b, 1)
check(route(a)["found"], "restoring a wireless link restores an active route")

request("/api/mesh/history?" + urllib.parse.urlencode({"device": a, "fromUtc": to_time, "toUtc": old_start}), expected=400)
request("/api/mesh/history?" + urllib.parse.urlencode({"device": a, "fromUtc": "invalid", "toUtc": to_time}), expected=400)
request("/api/mesh/history?" + urllib.parse.urlencode({"device": a, "fromUtc": from_time, "toUtc": to_time, "limit": 0}), expected=400)
request("/api/mesh/history?" + urllib.parse.urlencode({"device": "UNKNOWN-" + suffix, "fromUtc": from_time, "toUtc": to_time}), expected=404)
check(True, "invalid ranges, timestamps, limits and unknown history devices are rejected")
request("/api/mesh/route?" + urllib.parse.urlencode({"from": a, "algorithm": "invalid"}), expected=400)
request("/api/mesh/route?" + urllib.parse.urlencode({"from": "UNKNOWN-" + suffix}), expected=404)
request("/api/mesh/links", {"source": a, "target": b, "cost": -1, "enabled": True}, expected=400, method="PUT")
request("/api/mesh/links", {"source": a, "target": a, "cost": 1, "enabled": True}, expected=400, method="PUT")
check(True, "invalid route algorithms and wireless graph edits are rejected")
print(f"{checks} mesh HTTP checks passed")
