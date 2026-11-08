# Toolchain (T001)

Versions on the development machine when CaseDesk was built (8 Nov 2026).

| Tool | Version | How installed | Needed for |
| --- | --- | --- | --- |
| .NET SDK | 10.0.403 | Installer | Building everything. Targets **net462** (plugins + plugin tests, via `Microsoft.NETFramework.ReferenceAssemblies`) and **net8.0** (Functions, Functions tests, seeder) |
| .NET Framework runtime | 4.8 (Windows) | Ships with Windows | Running the net462 plugin tests |
| Power Platform CLI (`pac`) | 2.14.1 | .NET global tool: `dotnet tool install --global Microsoft.PowerApps.CLI.Tool` | `pac solution`, `pac pcf`, `pac auth`, Plugin Registration Tool (`pac tool prt`) |
| Node.js | 24.19.0 | Installer | PCF build and Jest tests (CI uses Node 20) |
| npm | 11.17.0 | Ships with Node | PCF packages |
| Azure CLI | not installed | — | Deploying `functions/infra/main.bicep` only |
| Azure Functions Core Tools v4 | not installed | — | Publishing the Function app only |

Azure CLI and Functions Core Tools are not needed to build or test. Install them only to deploy
the Azure side:

```powershell
winget install Microsoft.AzureCLI
npm install -g azure-functions-core-tools@4 --unsafe-perm true
```

Update `pac` with `dotnet tool update --global Microsoft.PowerApps.CLI.Tool`.

CI runners (GitHub Actions) install their own toolchain: .NET 8.x and 10.x via
`actions/setup-dotnet`, Node 20 via `actions/setup-node`, and `pac` via
`microsoft/powerplatform-actions/actions-install@v1`.
