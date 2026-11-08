using System.ServiceModel;
using CaseDesk.Seeder;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

Options options;
try
{
    options = Options.Parse(args);
}
catch (ArgumentException ex)
{
    Log.Error(ex.Message);
    Console.WriteLine(Options.Usage);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(Options.Usage);
    return 0;
}

if (string.IsNullOrWhiteSpace(options.Connection))
{
    Log.Error($"No connection string. Pass --connection or set {Constants.ConnectionEnvVar}.");
    Console.WriteLine(Options.Usage);
    return 2;
}

Console.WriteLine("CaseDesk.Seeder" + (options.DryRun ? "  [DRY RUN - read only]" : ""));

using var client = new ServiceClient(options.Connection);
if (!client.IsReady)
{
    // LastError never contains the secret; the connection string itself is never printed.
    Log.Error($"Could not connect to Dataverse: {client.LastError}");
    return 1;
}

Console.WriteLine($"Connected to {client.ConnectedOrgFriendlyName} ({client.ConnectedOrgUriActual?.GetLeftPart(UriPartial.Authority)})");
Console.WriteLine($"As {Seeder.WhoAmI(client)}");

try
{
    new Seeder(client, options.DryRun).Run(options.SlaDemo);
}
catch (FaultException<OrganizationServiceFault> ex)
{
    Log.Error(ex.Detail?.Message ?? ex.Message);
    Log.Info("If a cd_ table or column is unknown, import the CaseDesk solution first (quickstart.md §2).");
    return 1;
}
catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
{
    Log.Error(ex.Message);
    return 1;
}

Log.Summary(options.DryRun);
return Log.Warnings > 0 ? 3 : 0;
