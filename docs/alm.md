# ALM: environments, secrets and deployment

CaseDesk follows constitution VI: one solution (`CaseDesk`, publisher prefix `cd_`), source
of truth unpacked under `/solution`, **managed** downstream, imported by pipeline only.

## Environments

| Environment | Type | Solution | Who changes it |
| --- | --- | --- | --- |
| Dev | Developer plan | Unmanaged `CaseDesk.zip` (imported by hand, then edited in the maker portal or in source) | Developers |
| UAT | Second developer environment or trial | Managed `CaseDesk_managed.zip` | `deploy-uat.yml` only |
| (later) AppSource | — | Managed, packaged with `pac package` | Release process |

Environment data that is not solution customisation is created per environment: business
units, the `CaseDesk Supervisors` team and demo data (seeder), service endpoints (Plugin
Registration Tool, see [caseclosed-endpoints.md](caseclosed-endpoints.md)), and connections.

## Pipelines

| Workflow | Trigger | Does |
| --- | --- | --- |
| `.github/workflows/ci.yml` (**CI**) | pull requests; pushes to any branch except `main`; called by Deploy UAT | Tests plugins (windows, net462), Functions (net8), PCF (Jest + build); then packs the solution and uploads artifact `solution` (both zips) |
| `.github/workflows/deploy-uat.yml` (**Deploy UAT**) | push to `main`, manual | Runs CI as a reusable workflow; when green, job `deploy` (environment `uat`) fills the deployment settings, imports `CaseDesk_managed.zip` and publishes |

The comment at the top of `deploy-uat.yml` explains why it uses a reusable workflow and not
`workflow_run`. Protect `main` with required status checks from the CI jobs on pull
requests.

## GitHub secrets

Store these as **environment secrets on `uat`** (preferred) or as repository secrets.

| Secret | Value |
| --- | --- |
| `PP_APP_ID` | Application (client) ID of the deployment app registration |
| `PP_CLIENT_SECRET` | Client secret of that app registration |
| `PP_TENANT_ID` | Entra ID tenant ID |
| `PP_UAT_URL` | UAT environment URL, e.g. `https://casedesk-uat.crm.dynamics.com` |

## GitHub variables (non-secret, environment `uat`)

`deploy-uat.yml` writes these into `solution/deploymentSettings.uat.json` before import. An
empty variable keeps the file's value, and the step warns when a value is still missing.

| Variable | Fills |
| --- | --- |
| `CD_SENDER_QUEUE_EMAIL` | Env var `cd_SenderQueueEmail` (From address for customer emails) |
| `CD_CASECLOSED_TRANSPORT` | Env var `cd_CaseClosedTransport` (`ServiceBus` / `Webhook` / `None`) |
| `CD_SUPERVISOR_TEAM_NAME` | Env var `cd_SupervisorTeamName` (optional) |
| `CD_AUTO_CLOSE_DAYS` | Env var `cd_AutoCloseDays` (optional; e.g. `0` for testing) |
| `CD_DATAVERSE_CONNECTION_ID` | Connection reference `cd_sharedcommondataserviceforapps` → ID of a Dataverse connection in UAT owned by the application user or a service account |

The deployment settings file in the repo contains placeholders only. No secret goes into it.
Endpoint secrets (SAS key, Function key) are entered on the service endpoint registration in
each environment.

## App registration and application user (UAT)

1. **Entra ID → App registrations → New registration** (`CaseDesk Deploy`, single tenant).
   Under *Certificates & secrets*, create a client secret and put it in `PP_CLIENT_SECRET`.
   Copy the client ID to `PP_APP_ID` and the tenant ID to `PP_TENANT_ID`. No API permissions
   are needed for Dataverse server-to-server auth.
2. **Power Platform admin centre → UAT environment → Settings → Users + permissions →
   Application users → New app user**. Pick the app registration and the root business unit.
   Assign **System Administrator**, or a custom deploy role with solution import, customisation
   and plugin registration privileges (least privilege, more upkeep).
3. Track the client secret's expiry (rotate it before it lapses, then update the GitHub
   secret). A certificate or federated credential can replace the secret later.
4. The same app user can run the seeder in UAT with
   `AuthType=ClientSecret;Url=…;ClientId=…;ClientSecret=…`.

## First import and `pac solution sync`

The solution XML in `/solution/src` was written by hand, so the **first import into Dev is the
real validation** (research R1). After it succeeds:

```powershell
pac auth create --name dev --environment https://<dev>.crm.dynamics.com
cd solution
pac solution sync
```

`pac solution sync` exports the solution from Dev and unpacks it over `/solution/src`, so the
repo matches the XML that Dataverse normalises (attribute ordering, generated ids, flow
`.json.data.xml`). Commit the result. Repeat after any change made in the maker portal.

## Versioning the solution

- The version lives in `solution/src/Other/Solution.xml` (`<Version>major.minor.build.revision</Version>`).
- Bump **minor** for new components, **build** for fixes. UAT shows a managed import as an
  upgrade only when the version is higher.
- Optional CI automation: set the revision to the run number before building, e.g.
  `pac solution version --buildversion ${{ github.run_number }}` (run in `solution/`) or
  `--strategy gittags`. Keep major/minor manual.
- Tag the commit that went to UAT (`casedesk-v1.2.0`) so a managed version can be traced to
  source.
- Managed import uses `force-overwrite: true` and publishes. Removing components later needs an
  *upgrade* (stage for upgrade / apply upgrade) rather than an update. Switch the import to
  `stage-and-upgrade: true` when that happens.
