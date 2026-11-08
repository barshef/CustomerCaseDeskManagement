namespace CaseDesk.Seeder;

/// <summary>Command-line options (deliberately simple parsing; no extra dependency).</summary>
public sealed class Options
{
    public string? Connection { get; private set; }
    public bool DryRun { get; private set; }
    public bool SlaDemo { get; private set; }
    public bool ShowHelp { get; private set; }

    public const string Usage = """
        CaseDesk.Seeder - idempotent demo data for CaseDesk (Dataverse)

        Usage:
          dotnet run --project tools/CaseDesk.Seeder -- [options]

        Options:
          -c, --connection <string>  Dataverse connection string. Defaults to the
                                     CASEDESK_DATAVERSE_CONNECTION environment variable.
          --dry-run                  Connect and read only; print what would change.
          --sla-demo                 After seeding, move the two newest open seed cases into
                                     the SLA warning window and past due (badge amber / red).
          -h, --help                 Show this help.

        Connection string examples:
          Interactive (dev):
            AuthType=OAuth;Url=https://<org>.crm.dynamics.com;LoginPrompt=Auto;
            AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri=http://localhost
          Service principal (CI):
            AuthType=ClientSecret;Url=https://<org>.crm.dynamics.com;
            ClientId=<app id>;ClientSecret=<secret>
        """;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--connection":
                case "-c":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--connection needs a value.");
                    o.Connection = args[++i];
                    break;
                case "--dry-run":
                    o.DryRun = true;
                    break;
                case "--sla-demo":
                    o.SlaDemo = true;
                    break;
                case "-h":
                case "--help":
                case "/?":
                    o.ShowHelp = true;
                    break;
                default:
                    if (a.StartsWith("--connection=", StringComparison.OrdinalIgnoreCase))
                    {
                        o.Connection = a["--connection=".Length..];
                        break;
                    }
                    throw new ArgumentException($"Unknown option '{a}'. Use --help.");
            }
        }

        if (string.IsNullOrWhiteSpace(o.Connection))
            o.Connection = Environment.GetEnvironmentVariable(Constants.ConnectionEnvVar);

        return o;
    }
}
