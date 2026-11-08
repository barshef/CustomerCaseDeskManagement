using System;
using FakeXrmEasy;
using FakeXrmEasy.FakeMessageExecutors;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace CaseDesk.Plugins.Tests
{
    /// <summary>FakeXrmEasy v1 has no RetrievePrincipalAccess support; return fixed rights.</summary>
    internal sealed class FakeAccessExecutor : IFakeMessageExecutor
    {
        private readonly AccessRights _rights;

        public FakeAccessExecutor(AccessRights rights)
        {
            _rights = rights;
        }

        public bool CanExecute(OrganizationRequest request) => request is RetrievePrincipalAccessRequest;

        public OrganizationResponse Execute(OrganizationRequest request, XrmFakedContext ctx)
        {
            var response = new RetrievePrincipalAccessResponse();
            response.Results["AccessRights"] = _rights;
            return response;
        }

        public Type GetResponsibleRequestType() => typeof(RetrievePrincipalAccessRequest);
    }
}
