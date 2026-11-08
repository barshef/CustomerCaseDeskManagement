# Solution component IDs

GUIDs of the hand-authored components in `solution/src` (generated once with `[guid]::NewGuid()`).
Keep them stable: Dataverse matches components by these IDs on every import, so changing one
creates a duplicate component instead of updating the existing one.

Components identified by name rather than GUID: tables `cd_case`, `cd_casenote`, `cd_slapolicy`,
`cd_refundapproval`; global choices `cd_casecategory`, `cd_casepriority`, `cd_notetype`,
`cd_refunddecision`; app module and sitemap `cd_casedesk`; Custom API `cd_EscalateCase`;
the six `cd_*` environment variable definitions; connection references
`cd_sharedcommondataserviceforapps`, `cd_sharedoffice365`, `cd_sharedapprovals`.

| Kind | Table | Component | ID |
| --- | --- | --- | --- |
| System form (main) | cd_case | Case main form (Timeline, Notes + Refund Approvals sub-grids, SLA badge PCF on cd_duedate) | `e51506dd-23c7-4594-8678-2b59ba4163a6` |
| System form (main) | cd_casenote | Case Note main form | `2b84dddd-7e6e-420d-a632-7705de1281f9` |
| System form (main) | cd_slapolicy | SLA Policy main form | `985c9188-5b68-40a0-b46b-783a6bf21aed` |
| System form (main) | cd_refundapproval | Refund Approval main form | `df5bd7d5-dedb-4c6b-a51d-bebc28671314` |
| View | cd_case | Active Cases (default) | `ce74676f-4c87-4058-a684-7ce4661ab724` |
| View | cd_case | Inactive Cases | `ce247302-27dc-4fbe-97e6-e602c6e0d097` |
| View | cd_case | My Open Cases (ordered by due date) | `a9d1ee2c-a1a0-4aaa-9892-3d696a834277` |
| View | cd_case | SLA Breaches This Week | `bc023c42-94e7-40ad-b4c9-454f2bf3b61e` |
| View | cd_case | Resolved Cases | `7788623a-d20b-4917-bf5c-be8198437704` |
| View | cd_case | Quick Find | `e1427fbe-658e-4cbe-b0e8-59f0c5dfc7aa` |
| View | cd_case | Lookup | `16dd9d72-5165-482d-a118-3d1a18daf385` |
| View | cd_case | Associated | `648f2c65-4ad1-4049-9321-275d19ea54ae` |
| View | cd_case | Advanced Find | `1ced9981-64f5-4564-873f-ec71ac602a5a` |
| View | cd_casenote | Active Case Notes | `92ff8d45-ccec-4a04-93e5-8e6af410e35e` |
| View | cd_casenote | Inactive Case Notes | `9e936da1-bc65-4a51-92f5-fa4947c3f65c` |
| View | cd_casenote | Quick Find | `a8ca2aae-c077-46f0-8c7a-86a43b4f2721` |
| View | cd_casenote | Lookup | `77a653a6-454c-438e-96ca-c31981d67695` |
| View | cd_casenote | Associated (used by Case form Notes sub-grid) | `0236b36b-5554-48b8-903a-161253bba168` |
| View | cd_casenote | Advanced Find | `bc044223-edda-4028-b080-e3588d9b0821` |
| View | cd_slapolicy | Active SLA Policies | `0ba36199-8be4-4cc6-ae2c-7f38e2095b43` |
| View | cd_slapolicy | Inactive SLA Policies | `ca7d3404-b4d3-405e-ac23-d1ded14cb637` |
| View | cd_slapolicy | Quick Find | `5118dc13-0d7e-40b4-918d-cb4b9e07ef35` |
| View | cd_slapolicy | Lookup | `4a380cff-ab07-432e-80cc-6e36c160348c` |
| View | cd_slapolicy | Associated | `5eac8dfb-52e9-41b0-a6ff-b47f44b4f815` |
| View | cd_slapolicy | Advanced Find | `d2fd6700-8752-4e04-a86a-945322da23db` |
| View | cd_refundapproval | Active Refund Approvals | `187843e8-ae19-458e-a9a6-c9abca6a7c1b` |
| View | cd_refundapproval | Inactive Refund Approvals | `bfdcf721-f50c-4d92-8f8a-312132d42b20` |
| View | cd_refundapproval | Pending Refund Approvals | `249dcb34-5223-4a4d-8b10-56692b6587f7` |
| View | cd_refundapproval | Quick Find | `7fee56f5-5af0-4925-99e0-2c4ff49e1c47` |
| View | cd_refundapproval | Lookup | `ccab87e5-5278-46a7-8c1b-9fb475e1d739` |
| View | cd_refundapproval | Associated (used by Case form Refund Approvals sub-grid) | `657852d6-afc3-422e-a2a8-b7f4397863cf` |
| View | cd_refundapproval | Advanced Find | `e9f3cea3-cc27-43ff-8a2b-aa5499140335` |
| Chart (savedqueryvisualization) | cd_case | Open Cases by Priority | `ed414c25-f988-4faf-b648-31d099a12bfe` |
| Chart (savedqueryvisualization) | cd_case | SLA Breaches by Owner | `96e547d1-1f91-4076-a2d3-f3433e071a36` |
| Chart (savedqueryvisualization) | cd_case | SLA Breaches by Priority | `04db2878-8dc5-451d-be48-c7040b5f1c7e` |
| Dashboard (systemform type 0) | — | CaseDesk Agent | `8aa5500f-cbb8-4067-8324-ca57cd98b69e` |
| Dashboard (systemform type 0) | — | CaseDesk Supervisor | `9ca3e552-20e0-4b98-bccb-1ebf8eea47bb` |
| Security role | — | CaseDesk Agent | `d03910ad-e39d-4d0a-8162-a068b4d7e861` |
| Security role | — | CaseDesk Supervisor | `399d12be-2523-4d4f-bd1f-6c2dbcffe58a` |
| Plugin assembly | — | CaseDesk.Plugins, Version=1.0.0.0, Culture=neutral, PublicKeyToken=4084e1bede633e50 | `d27c16a9-0827-40ce-b313-82aa98778c6c` |
| Plugin type | — | CaseDesk.Plugins.CasePreCreatePlugin | `5d08fcdf-8e39-4fb0-955e-d77d11350589` |
| Plugin type | — | CaseDesk.Plugins.CasePreUpdatePlugin | `63257b08-2733-49a1-812f-0c8fec9897aa` |
| Plugin type | — | CaseDesk.Plugins.PublishCaseClosedPlugin | `c0ae17c8-990e-4a85-a590-4786275bca96` |
| Plugin type | — | CaseDesk.Plugins.EscalateCasePlugin (Custom API cd_EscalateCase plugintypeexportkey) | `7a777763-bbbd-4a4c-aace-ac6f87a13ca0` |
| SDK step | cd_case | S1 Create, PreOperation, Sync | `d802fe6b-7e54-42b2-bfe3-b864b4ad461f` |
| SDK step | cd_case | S2 Update, PreOperation, Sync | `06cc5c65-bc6a-470e-a197-425bc6fd2263` |
| SDK step | cd_case | S3 Update, PostOperation, Async | `1eacde87-49d9-4c58-8667-9ccf8ec7ba45` |
| Step image | cd_case | S2 PreImage | `84535399-58bc-4884-a02d-62dfaa1cb1f3` |
| Step image | cd_case | S3 PreImage | `4e907000-2124-4865-95af-f8aafbbc2f89` |
| Step image | cd_case | S3 PostImage | `4edb3960-fa25-432e-a224-ea1dc22533fa` |
| Web resource | — | cd_/scripts/case.ribbon.js | `11983757-6455-4c4c-9b93-ab0a73a30619` |
| Web resource | — | cd_/html/escalate.html | `7857931f-20dd-436a-a68a-7de3d04ba0fc` |

Well-known IDs used in the app module role map: System Administrator
`627090ff-40a3-4053-8790-584edc5be201`, System Customizer `119f245c-3cc8-4b62-b31c-d1a046ced15d`.
SDK message IDs: Create `9ebdbb1b-ea3e-db11-86a7-000a3a5473e8`, Update `20bebb1b-ea3e-db11-86a7-000a3a5473e8`.
