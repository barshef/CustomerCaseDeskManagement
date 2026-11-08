using System;
using System.Collections.Generic;
using CaseDesk.Plugins.Domain;
using FakeXrmEasy;
using Microsoft.Xrm.Sdk;
using C = CaseDesk.Plugins.Domain.Schema.Case;

namespace CaseDesk.Plugins.Tests
{
    internal static class TestData
    {
        public static readonly Guid AccountId = Guid.NewGuid();

        public static Entity SlaPolicy(int priority, int resolutionHours, int warningHours)
        {
            var e = new Entity(Schema.SlaPolicy.EntityName, Guid.NewGuid());
            e[Schema.SlaPolicy.Priority] = new OptionSetValue(priority);
            e[Schema.SlaPolicy.ResolutionHours] = resolutionHours;
            e[Schema.SlaPolicy.WarningHours] = warningHours;
            e["cd_name"] = Domain.Priority.Label(priority);
            return e;
        }

        /// <summary>The seed policies from data-model.md.</summary>
        public static List<Entity> AllPolicies() => new List<Entity>
        {
            SlaPolicy(Domain.Priority.Critical, 4, 1),
            SlaPolicy(Domain.Priority.High, 8, 2),
            SlaPolicy(Domain.Priority.Medium, 24, 2),
            SlaPolicy(Domain.Priority.Low, 72, 2),
        };

        public static Entity NewCase(int priority = Domain.Priority.High, int category = Domain.Category.Billing,
            string title = "Invoice wrong", bool withAccount = true)
        {
            var e = new Entity(C.EntityName, Guid.NewGuid());
            if (title != null) e[C.Title] = title;
            e[C.Priority] = new OptionSetValue(priority);
            e[C.Category] = new OptionSetValue(category);
            if (withAccount) e[C.Account] = new EntityReference("account", AccountId);
            return e;
        }

        /// <summary>A stored case as it would appear in a pre-image.</summary>
        public static Entity ExistingCase(int status, int priority = Domain.Priority.High,
            int category = Domain.Category.Billing, DateTime? createdOn = null)
        {
            var e = NewCase(priority, category);
            e[C.StatusCode] = new OptionSetValue(status);
            e[C.StateCode] = new OptionSetValue(CaseStatus.StateOf(status));
            e[C.CreatedOn] = createdOn ?? new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
            e[C.CaseNumber] = "CD-00042";
            return e;
        }

        public static Entity Note(Guid caseId, bool isOpen)
        {
            var e = new Entity(Schema.CaseNote.EntityName, Guid.NewGuid());
            e[Schema.CaseNote.Case] = new EntityReference(C.EntityName, caseId);
            e[Schema.CaseNote.IsOpen] = isOpen;
            e[Schema.CaseNote.Text] = "note";
            return e;
        }

        public static Entity Approval(Guid caseId, int decision, DateTime createdOn)
        {
            var e = new Entity(Schema.RefundApproval.EntityName, Guid.NewGuid());
            e[Schema.RefundApproval.Case] = new EntityReference(C.EntityName, caseId);
            e[Schema.RefundApproval.Decision] = new OptionSetValue(decision);
            e[Schema.RefundApproval.CreatedOn] = createdOn;
            return e;
        }

        public static Entity EnvVar(string schemaName, string defaultValue, string currentValue = null, List<Entity> into = null)
        {
            var def = new Entity("environmentvariabledefinition", Guid.NewGuid());
            def["schemaname"] = schemaName;
            def["defaultvalue"] = defaultValue;
            def["environmentvariabledefinitionid"] = def.Id;
            if (currentValue != null && into != null)
            {
                var val = new Entity("environmentvariablevalue", Guid.NewGuid());
                val["environmentvariabledefinitionid"] = def.ToEntityReference();
                val["value"] = currentValue;
                into.Add(val);
            }
            return def;
        }

        public static XrmFakedPluginExecutionContext UpdateContext(XrmFakedContext fake, Entity target, Entity preImage,
            Entity postImage = null, int stage = 20)
        {
            var ctx = fake.GetDefaultPluginContext();
            ctx.MessageName = "Update";
            ctx.Stage = stage;
            ctx.PrimaryEntityName = C.EntityName;
            ctx.PrimaryEntityId = target.Id;
            ctx.InputParameters = new ParameterCollection { { "Target", target } };
            ctx.PreEntityImages = new EntityImageCollection { { "PreImage", preImage } };
            if (postImage != null) ctx.PostEntityImages = new EntityImageCollection { { "PostImage", postImage } };
            return ctx;
        }

        /// <summary>Update target for a status change, as the UI sends it.</summary>
        public static Entity StatusChange(Entity existing, int newStatus)
        {
            var target = new Entity(C.EntityName, existing.Id);
            target[C.StatusCode] = new OptionSetValue(newStatus);
            target[C.StateCode] = new OptionSetValue(CaseStatus.StateOf(newStatus));
            return target;
        }
    }
}
