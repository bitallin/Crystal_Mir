# Crystal.GameCore

`Crystal.GameCore` is the GPL-3.0-only boundary for the future game simulation core. M1 only
defines runtime ports. The existing `Server/` implementation remains the source of truth and is
not moved or rewritten yet.

The project may depend on the .NET base class library and `Crystal.Shared`. It must not reference
EF Core, ASP.NET Core, WinForms, Control API, ZoneServer, or any concrete persistence/network
implementation.
