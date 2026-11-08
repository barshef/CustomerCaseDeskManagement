using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using static CaseDesk.Seeder.Constants;

namespace CaseDesk.Seeder;

/// <summary>
/// Idempotent seeding steps. Every step reads first, then creates or updates only what differs,
/// so re-running produces "unchanged" lines and no duplicates. With dry-run, nothing is written.
/// </summary>
public sealed class Seeder(IOrganizationService svc, bool dryRun)
{
    private readonly Dictionary<string, Guid> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> _contacts = new(StringComparer.OrdinalIgnoreCase);
    private Guid _rootBusinessUnitId;

    public void Run(bool slaDemo)
    {
        SeedBusinessUnits();
        SeedSupervisorTeam();
        SeedSlaPolicies();
        SeedAccounts();
        SeedContacts();
        SeedCases();
        if (slaDemo)
            RunSlaDemo();
    }

    // (a) Business units under the root BU -------------------------------------------------
    private void SeedBusinessUnits()
    {
        Log.Section("Business units");

        var rootQuery = new QueryExpression(Tables.BusinessUnit)
        {
            ColumnSet = new ColumnSet("name"),
            TopCount = 1,
        };
        rootQuery.Criteria.AddCondition("parentbusinessunitid", ConditionOperator.Null);
        var root = svc.RetrieveMultiple(rootQuery).Entities.FirstOrDefault()
                   ?? throw new InvalidOperationException("Root business unit not found.");
        _rootBusinessUnitId = root.Id;
        Log.Info($"root business unit: {root.GetAttributeValue<string>("name")}");

        foreach (var name in new[] { BusinessUnitA, BusinessUnitB })
        {
            var existing = FindByName(Tables.BusinessUnit, "name", name);
            if (existing is not null)
            {
                Log.Same($"business unit '{name}'");
                continue;
            }

            if (dryRun)
            {
                Log.WouldDo($"create business unit '{name}' under the root BU");
                continue;
            }

            var bu = new Entity(Tables.BusinessUnit)
            {
                ["name"] = name,
                ["parentbusinessunitid"] = new EntityReference(Tables.BusinessUnit, _rootBusinessUnitId),
            };
            svc.Create(bu);
            Log.Create($"business unit '{name}'");
        }
    }

    // (b) Owner team in the root BU with the CaseDesk Supervisor role -----------------------
    private void SeedSupervisorTeam()
    {
        Log.Section("Supervisor team");

        var teamQuery = new QueryExpression(Tables.Team) { ColumnSet = new ColumnSet("name", "teamtype") };
        teamQuery.Criteria.AddCondition("name", ConditionOperator.Equal, SupervisorTeamName);
        teamQuery.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, _rootBusinessUnitId);
        var team = svc.RetrieveMultiple(teamQuery).Entities.FirstOrDefault();

        Guid? teamId = team?.Id;
        if (team is not null)
        {
            Log.Same($"owner team '{SupervisorTeamName}'");
        }
        else if (dryRun)
        {
            Log.WouldDo($"create owner team '{SupervisorTeamName}' in the root BU");
        }
        else
        {
            teamId = svc.Create(new Entity(Tables.Team)
            {
                ["name"] = SupervisorTeamName,
                ["teamtype"] = new OptionSetValue(TeamTypeOwner),
                ["businessunitid"] = new EntityReference(Tables.BusinessUnit, _rootBusinessUnitId),
                ["description"] = "CaseDesk escalation owner team (created by CaseDesk.Seeder).",
            });
            Log.Create($"owner team '{SupervisorTeamName}'");
        }

        // Roles are copied into every BU; the team must get the root BU's copy.
        var roleQuery = new QueryExpression(Tables.Role) { ColumnSet = new ColumnSet("name") };
        roleQuery.Criteria.AddCondition("name", ConditionOperator.Equal, SupervisorRoleName);
        roleQuery.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, _rootBusinessUnitId);
        var role = svc.RetrieveMultiple(roleQuery).Entities.FirstOrDefault();
        if (role is null)
        {
            Log.Warn($"security role '{SupervisorRoleName}' not found in the root BU - import the CaseDesk solution, then re-run.");
            return;
        }

        if (teamId is null)
        {
            Log.WouldDo($"assign role '{SupervisorRoleName}' to '{SupervisorTeamName}'");
            return;
        }

        var linkQuery = new QueryExpression(Tables.TeamRoles) { ColumnSet = new ColumnSet(false), TopCount = 1 };
        linkQuery.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId.Value);
        linkQuery.Criteria.AddCondition("roleid", ConditionOperator.Equal, role.Id);
        if (svc.RetrieveMultiple(linkQuery).Entities.Count > 0)
        {
            Log.Same($"role '{SupervisorRoleName}' on team");
            return;
        }

        if (dryRun)
        {
            Log.WouldDo($"assign role '{SupervisorRoleName}' to '{SupervisorTeamName}'");
            return;
        }

        svc.Associate(Tables.Team, teamId.Value, new Relationship(TeamRolesRelationship),
            new EntityReferenceCollection { new EntityReference(Tables.Role, role.Id) });
        Log.Create($"role '{SupervisorRoleName}' assigned to '{SupervisorTeamName}'");
    }

    // (c) SLA policies: UpsertRequest on alternate key cd_slapolicy_priority_key ---------------
    private void SeedSlaPolicies()
    {
        Log.Section($"SLA policies (upsert by alternate key {SlaPolicyPriorityKey})");

        var query = new QueryExpression(Tables.SlaPolicy)
        {
            ColumnSet = new ColumnSet("cd_name", "cd_priority", "cd_resolutionhours", "cd_warninghours"),
        };
        var existing = svc.RetrieveMultiple(query).Entities
            .Where(e => e.GetAttributeValue<OptionSetValue>("cd_priority") is not null)
            .GroupBy(e => e.GetAttributeValue<OptionSetValue>("cd_priority").Value)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var seed in SeedData.SlaPolicies)
        {
            var label = $"{Priority.Label(seed.Priority)} {seed.ResolutionHours}h / warn {seed.WarningHours}h";
            if (existing.TryGetValue(seed.Priority, out var row)
                && row.GetAttributeValue<string>("cd_name") == seed.Name
                && row.GetAttributeValue<int?>("cd_resolutionhours") == seed.ResolutionHours
                && row.GetAttributeValue<int?>("cd_warninghours") == seed.WarningHours)
            {
                Log.Same($"SLA policy {label}");
                continue;
            }

            if (dryRun)
            {
                Log.WouldDo($"upsert SLA policy {label}");
                continue;
            }

            // Key values identify the row; on create the platform also writes them to the column.
            var target = new Entity(Tables.SlaPolicy, new KeyAttributeCollection
            {
                { "cd_priority", new OptionSetValue(seed.Priority) },
            })
            {
                ["cd_name"] = seed.Name,
                ["cd_resolutionhours"] = seed.ResolutionHours,
                ["cd_warninghours"] = seed.WarningHours,
            };

            var response = (UpsertResponse)svc.Execute(new UpsertRequest { Target = target });
            if (response.RecordCreated) Log.Create($"SLA policy {label}");
            else Log.Update($"SLA policy {label}");
        }
    }

    // (d) Accounts by accountnumber (not a key by default: query, then create/update) -------
    private void SeedAccounts()
    {
        Log.Section("Accounts (matched on accountnumber)");

        foreach (var seed in SeedData.Accounts)
        {
            var row = FindByName(Tables.Account, "accountnumber", seed.AccountNumber, "name", "emailaddress1");
            var label = $"{seed.AccountNumber} {seed.Name}";
            if (row is null)
            {
                if (dryRun) { Log.WouldDo($"create account {label}"); continue; }
                _accounts[seed.AccountNumber] = svc.Create(new Entity(Tables.Account)
                {
                    ["accountnumber"] = seed.AccountNumber,
                    ["name"] = seed.Name,
                    ["emailaddress1"] = seed.Email,
                });
                Log.Create($"account {label}");
                continue;
            }

            _accounts[seed.AccountNumber] = row.Id;
            if (row.GetAttributeValue<string>("name") == seed.Name
                && row.GetAttributeValue<string>("emailaddress1") == seed.Email)
            {
                Log.Same($"account {label}");
                continue;
            }

            if (dryRun) { Log.WouldDo($"update account {label}"); continue; }
            svc.Update(new Entity(Tables.Account, row.Id) { ["name"] = seed.Name, ["emailaddress1"] = seed.Email });
            Log.Update($"account {label}");
        }
    }

    // (d) Contacts by emailaddress1 ---------------------------------------------------------
    private void SeedContacts()
    {
        Log.Section("Contacts (matched on emailaddress1)");

        foreach (var seed in SeedData.Contacts)
        {
            var row = FindByName(Tables.Contact, "emailaddress1", seed.Email, "firstname", "lastname", "parentcustomerid");
            var label = $"{seed.FirstName} {seed.LastName} <{seed.Email}>";
            _accounts.TryGetValue(seed.AccountNumber, out var accountId);
            var parent = accountId == Guid.Empty ? null : new EntityReference(Tables.Account, accountId);

            if (row is null)
            {
                if (dryRun) { Log.WouldDo($"create contact {label}"); continue; }
                var contact = new Entity(Tables.Contact)
                {
                    ["firstname"] = seed.FirstName,
                    ["lastname"] = seed.LastName,
                    ["emailaddress1"] = seed.Email,
                };
                if (parent is not null) contact["parentcustomerid"] = parent;
                _contacts[seed.Email] = svc.Create(contact);
                Log.Create($"contact {label}");
                continue;
            }

            _contacts[seed.Email] = row.Id;
            var same = row.GetAttributeValue<string>("firstname") == seed.FirstName
                       && row.GetAttributeValue<string>("lastname") == seed.LastName
                       && (parent is null || row.GetAttributeValue<EntityReference>("parentcustomerid")?.Id == parent.Id);
            if (same) { Log.Same($"contact {label}"); continue; }

            if (dryRun) { Log.WouldDo($"update contact {label}"); continue; }
            var update = new Entity(Tables.Contact, row.Id) { ["firstname"] = seed.FirstName, ["lastname"] = seed.LastName };
            if (parent is not null) update["parentcustomerid"] = parent;
            svc.Update(update);
            Log.Update($"contact {label}");
        }
    }

    // (e) Sample cases, only when no seed case exists yet ---------------------------------------
    private void SeedCases()
    {
        Log.Section($"Sample cases (title prefix '{SeedCasePrefix}')");

        var existing = QuerySeedCases(openOnly: false, top: 1);
        if (existing.Count > 0)
        {
            Log.Same($"seed cases already exist - skipping (delete cases titled '{SeedCasePrefix} ...' to re-seed)");
            return;
        }

        foreach (var seed in SeedData.Cases)
        {
            var label = $"{seed.Title} ({Category.Label(seed.Category)}, {Priority.Label(seed.Priority)})";
            if (dryRun) { Log.WouldDo($"create case {label}"); continue; }

            if (!_accounts.TryGetValue(seed.AccountNumber, out var accountId))
            {
                Log.Warn($"account {seed.AccountNumber} missing - skipped case {seed.Title}");
                continue;
            }

            // No cd_duedate / cd_warningon / cd_slapolicyid: the pre-create plugin sets them (FR3).
            var entity = new Entity(Tables.Case)
            {
                ["cd_name"] = seed.Title,
                ["cd_description"] = seed.Description,
                ["cd_category"] = new OptionSetValue(seed.Category),
                ["cd_priority"] = new OptionSetValue(seed.Priority),
                ["cd_accountid"] = new EntityReference(Tables.Account, accountId),
            };
            if (seed.ContactEmail is not null && _contacts.TryGetValue(seed.ContactEmail, out var contactId))
                entity["cd_contactid"] = new EntityReference(Tables.Contact, contactId);
            if (seed.RefundAmount is { } amount)
                entity["cd_refundamount"] = new Money(amount);

            var id = svc.Create(entity);
            var created = svc.Retrieve(Tables.Case, id, new ColumnSet("cd_casenumber", "cd_duedate"));
            var due = created.GetAttributeValue<DateTime?>("cd_duedate");
            Log.Create($"{created.GetAttributeValue<string>("cd_casenumber") ?? id.ToString()} {label}"
                       + (due is null ? " - WARNING: no due date (is plugin step S1 registered?)" : $" due {due:u}"));
        }
    }

    // (f) --sla-demo ----------------------------------------------------------------------------
    private void RunSlaDemo()
    {
        Log.Section("SLA demo (--sla-demo)");
        Log.Info("Writes cd_duedate / cd_warningon directly. This bypasses the SLA plugin only because");
        Log.Info("the pre-update step (S2) filters on statecode, statuscode, cd_priority and cd_category,");
        Log.Info("not on cd_duedate. That is intended for the demo; a priority change recalculates both.");

        var cases = QuerySeedCases(openOnly: true, top: 2);
        if (cases.Count == 0)
        {
            Log.Warn("no open seed cases found - run without --dry-run first so cases exist.");
            return;
        }

        var now = DateTime.UtcNow;
        var plans = new List<(Entity Case, DateTime Due, DateTime Warn, string State)>
        {
            (cases[0], now.AddMinutes(90), now.AddMinutes(-30), "AMBER (warning window: due in 90 min, warning began 30 min ago)"),
        };
        if (cases.Count > 1)
            plans.Add((cases[1], now.AddHours(-1), now.AddHours(-3), "RED (overdue by 1 h)"));
        else
            Log.Warn("only one open seed case - no overdue example.");

        foreach (var (c, due, warn, state) in plans)
        {
            var label = $"{c.GetAttributeValue<string>("cd_casenumber")} {c.GetAttributeValue<string>("cd_name")} -> {state}";
            if (dryRun) { Log.WouldDo(label); continue; }

            svc.Update(new Entity(Tables.Case, c.Id)
            {
                ["cd_duedate"] = due,
                ["cd_warningon"] = warn,
                ["cd_slawarningsent"] = false, // let the SLA warning flow (FR7) pick it up
            });
            Log.Update(label);
        }
    }

    // Helpers -----------------------------------------------------------------------------------

    private Entity? FindByName(string table, string column, string value, params string[] columns)
    {
        var q = new QueryExpression(table) { ColumnSet = new ColumnSet(columns), TopCount = 1 };
        q.Criteria.AddCondition(column, ConditionOperator.Equal, value);
        return svc.RetrieveMultiple(q).Entities.FirstOrDefault();
    }

    /// <summary>
    /// Seed cases, newest first. '[' is a wildcard in SQL LIKE, so the server filter avoids it
    /// ('%Seed]%') and the exact prefix is checked client side.
    /// </summary>
    private List<Entity> QuerySeedCases(bool openOnly, int top)
    {
        var q = new QueryExpression(Tables.Case)
        {
            ColumnSet = new ColumnSet("cd_name", "cd_casenumber", "createdon"),
            TopCount = 200,
        };
        q.Criteria.AddCondition("cd_name", ConditionOperator.Like, "%Seed]%");
        if (openOnly)
        {
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, CaseStatus.StateActive);
            q.Criteria.AddCondition("statuscode", ConditionOperator.NotEqual, CaseStatus.Resolved);
        }
        q.AddOrder("createdon", OrderType.Descending);

        return svc.RetrieveMultiple(q).Entities
            .Where(e => (e.GetAttributeValue<string>("cd_name") ?? "").StartsWith(SeedCasePrefix, StringComparison.Ordinal))
            .Take(top)
            .ToList();
    }

    public static string WhoAmI(IOrganizationService svc)
    {
        var who = (WhoAmIResponse)svc.Execute(new WhoAmIRequest());
        var user = svc.Retrieve("systemuser", who.UserId, new ColumnSet("fullname", "domainname"));
        return $"{user.GetAttributeValue<string>("fullname")} ({user.GetAttributeValue<string>("domainname")})";
    }
}
