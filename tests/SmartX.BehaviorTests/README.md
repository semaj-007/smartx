# Real-time behavior checks

This dependency-free console runner validates the real-time engine's observable
behavior: FIFO ingestion, immediate critical alerts, unique alert recovery,
ordered history with timestamp collisions, device registration constraints,
command undo, invalid packet rejection, bounded memory, filters, and learned
recommendations. Failed checks print their names and return a nonzero exit code.

From the repository root, with the .NET 10 SDK installed:

```sh
dotnet run --project tests/SmartX.BehaviorTests/SmartX.BehaviorTests.csproj --artifacts-path /workspace/.smartx-tests
```

The artifacts directory keeps generated outputs outside the checkout. The runner
uses only the actual Core implementation and does not require NuGet test packages,
an API process, a database, or a Windows desktop session. API/device HTTP integration
and the desktop application require their separate smoke checks.
