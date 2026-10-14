"""End-to-end device HTTP checks. Run against a dedicated, running SmartX API."""
import concurrent.futures
from datetime import datetime
import json
import time
import urllib.error
import urllib.request
import uuid
import os

BASE = os.environ.get("SMARTX_API_URL", "http://localhost:5080/").rstrip("/")
checks = 0


def request(path, payload=None, expected=200):
    data = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(BASE + path, data=data, headers={"Content-Type": "application/json"})
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


def until(predicate, timeout=5):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = predicate()
        if value:
            return value
        time.sleep(0.05)
    raise AssertionError("Timed out waiting for background processing")


suffix = uuid.uuid4().hex[:8]
devices = {}
for index, (name, category) in enumerate((("moisture", "SoilMoisture"), ("pump", "PowerMeter"), ("valve", "ValveState"))):
    device_id = name + "-" + suffix
    devices[name] = device_id
    mac = "02:" + ":".join(f"{byte:02X}" for byte in bytes.fromhex(suffix) + bytes([index]))
    request("/api/sensors/register", {"deviceId": device_id, "macAddress": mac,
        "location": "HTTP smoke " + suffix, "category": category}, 201)
check(len(devices) == 3, "virtual devices register over HTTP")
upload = (b'--smoke\r\nContent-Disposition: form-data; name="file"; filename="smoke.txt"\r\n'
          b'Content-Type: text/plain\r\n\r\nsmartx-smoke\r\n--smoke--\r\n')
with urllib.request.urlopen(urllib.request.Request(BASE + "/api/sensors/" + devices["valve"] + "/attachment",
        data=upload, headers={"Content-Type": "multipart/form-data; boundary=smoke"})) as response:
    attachment = json.load(response)
check(attachment["sizeBytes"] == 12 and any(s["deviceId"] == devices["valve"] and s["attachmentFileName"] == attachment["fileName"]
      for s in request("/api/sensors")), "Part 1 attachment uploads retain their metadata")
request("/api/sensors/register", {"deviceId": devices["moisture"], "macAddress": mac,
    "location": "Duplicate", "category": "SoilMoisture"}, 409)
check(True, "duplicate identity rejected")


def ingest(name, category, value_type, value, expected=202):
    return request("/api/telemetry/ingest", {"deviceId": devices.get(name, name), "category": category,
        "valueType": value_type, "value": value, "unit": ""}, expected)


ingest("unknown-" + suffix, "SoilMoisture", "float", "20", 404)
ingest("moisture", "Temperature", "float", "20", 400)
ingest("moisture", "SoilMoisture", "float", "NaN", 400)
ingest("pump", "PowerMeter", "int", "99999999999999999999", 400)
request("/api/telemetry/ingest", {"deviceId": devices["moisture"]}, 400)
request("/api/telemetry/ingest", {"deviceId": devices["moisture"], "valueType": "float", "value": "20"}, 400)
request("/api/telemetry/ingest", {"deviceId": devices["moisture"], "category": "SoilMoisture", "valueType": "float"}, 400)
check(True, "unknown, category mismatch, nonfinite and overflowing readings rejected")
check(True, "incomplete payloads cannot fabricate a zero reading")
first = ingest("moisture", "SoilMoisture", "float", "45")
until(lambda: any(p["packetId"] == first["packetId"] for p in request("/api/realtime/devices/" + devices["moisture"] + "/timeline")))
critical = ingest("moisture", "SoilMoisture", "float", "15")
ingest("moisture", "SoilMoisture", "float", "14")
state = request("/api/realtime/dashboard?query=" + devices["moisture"])
check(len(state["devices"][0]["activeAlerts"]) == 1, "repeated critical alerts remain unique")
check(any(p["packetId"] == critical["packetId"] and p["isCritical"] for p in state["recentTelemetry"]), "critical readings processed immediately")
ingest("moisture", "SoilMoisture", "float", "50")
until(lambda: not request("/api/realtime/dashboard?query=" + devices["moisture"])["devices"][0]["activeAlerts"])
check(True, "healthy telemetry clears critical state")

with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
    list(pool.map(lambda i: ingest("pump", "PowerMeter", "int", str(100 + i)), range(80)))
until(lambda: len(request("/api/realtime/devices/" + devices["pump"] + "/timeline")) >= 80)
timeline = request("/api/realtime/devices/" + devices["pump"] + "/timeline")
times = [datetime.fromisoformat(p["timestampUtc"].replace("Z", "+00:00")) for p in timeline]
check(len(timeline) == 80 and times == sorted(times),
    "concurrent HTTP ingestion is retained chronologically")

request("/api/realtime/commands", {"deviceId": devices["valve"], "action": "TurnOn"})
request("/api/realtime/commands", {"deviceId": "*", "action": "AllValvesOff"})
check(not request("/api/realtime/dashboard?query=" + devices["valve"])["devices"][0]["isOn"], "bulk valve shutdown applies")
request("/api/realtime/commands/undo", {})
check(request("/api/realtime/dashboard?query=" + devices["valve"])["devices"][0]["isOn"], "stack undo restores previous valve state")
request("/api/realtime/commands", {"deviceId": devices["moisture"], "action": "TurnOn"}, 400)
check(True, "commands reject non-actuator devices")

for _ in range(3):
    ingest("moisture", "SoilMoisture", "float", "50")
    until(lambda: not request("/api/realtime/dashboard?query=" + devices["moisture"])["devices"][0]["activeAlerts"])
    ingest("moisture", "SoilMoisture", "float", "12")
    request("/api/realtime/searches", {"query": devices["moisture"]}, 204)
    request("/api/realtime/searches", {"query": devices["pump"]}, 204)
    request("/api/realtime/commands", {"deviceId": devices["pump"], "action": "TurnOn"})
    request("/api/realtime/commands", {"deviceId": devices["pump"], "action": "TurnOff"})
insights = request("/api/realtime/dashboard")["suggestedActions"]
check(any(i["deviceId"] == devices["pump"] and i["action"] == "TurnOn" and i["evidenceCount"] >= 3 for i in insights),
    "operator search/command patterns produce evidence-backed suggestions")
check(not request("/api/realtime/dashboard?query=" + devices["pump"])["devices"][0]["isOn"], "suggestions do not actuate automatically")
filtered = request("/api/realtime/dashboard?category=SoilMoisture&alertsOnly=true")["devices"]
check(any(d["deviceId"] == devices["moisture"] for d in filtered) and all(d["category"] == "SoilMoisture" and d["activeAlerts"] for d in filtered),
    "category and active-alert filters combine")

fleet_size = request("/api/realtime/dashboard")["metrics"]["registeredDevices"]
request("/api/realtime/simulation/start", {})
until(lambda: request("/api/realtime/simulation")["sentPackets"] > 0)
sim = request("/api/realtime/simulation")
check(sim["running"] and sim["failedRequests"] == 0, "hosted virtual fleet sends successful API requests")
request("/api/realtime/commands", {"deviceId": devices["valve"], "action": "TurnOff"})
request("/api/realtime/commands", {"deviceId": devices["pump"], "action": "TurnOn"})
until(lambda: request("/api/realtime/simulation")["sentPackets"] >= sim["sentPackets"] + 2 * fleet_size)
check(not request("/api/realtime/dashboard?query=" + devices["valve"])["devices"][0]["isOn"] and
      request("/api/realtime/dashboard?query=" + devices["pump"])["devices"][0]["isOn"],
      "manual desired states survive live simulator telemetry")
request("/api/realtime/commands/undo", {})
sent_after_undo = request("/api/realtime/simulation")["sentPackets"]
until(lambda: request("/api/realtime/simulation")["sentPackets"] >= sent_after_undo + fleet_size)
check(not request("/api/realtime/dashboard?query=" + devices["pump"])["devices"][0]["isOn"],
      "undo desired state survives live simulator telemetry")
request("/api/realtime/simulation/stop", {})
time.sleep(0.2)
sent = request("/api/realtime/simulation")["sentPackets"]
time.sleep(1.2)
check(request("/api/realtime/simulation")["sentPackets"] == sent, "simulation stops cleanly")
until(lambda: "Disconnected" in request("/api/realtime/dashboard?query=" + devices["valve"])["devices"][0]["activeAlerts"], timeout=20)
check(request("/api/realtime/dashboard?query=" + devices["valve"])["devices"][0]["activeAlerts"].count("Disconnected") == 1,
      "hosted timeout detection creates one disconnect alert")
ingest("valve", "ValveState", "bool", "false")
until(lambda: "Disconnected" not in request("/api/realtime/dashboard?query=" + devices["valve"])["devices"][0]["activeAlerts"])
check(request("/api/realtime/dashboard?query=" + devices["valve"])["devices"][0]["isConnected"],
      "fresh device HTTP telemetry restores connectivity")
print(f"{checks} HTTP checks passed")
