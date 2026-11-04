# FullLock — Windows receiver

C# .NET 8 console app: receives phone input over UDP (or reads a DualShock 4 over USB) and drives a virtual Xbox 360 controller through ViGEmBus.

```bash
dotnet test F1Controller.sln
dotnet run --project F1Controller -- --help
```

Architecture, packet format and build details: [docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md).
