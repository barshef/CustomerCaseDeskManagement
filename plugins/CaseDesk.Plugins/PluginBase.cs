using System;
using System.Diagnostics;
using Microsoft.Xrm.Sdk;

namespace CaseDesk.Plugins
{
    /// <summary>Per-execution services, so plugin classes stay stateless (plugins are cached).</summary>
    public sealed class LocalPluginContext
    {
        public LocalPluginContext(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            ExecutionContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            Tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            Factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            UserService = Factory.CreateOrganizationService(ExecutionContext.UserId);
            SystemService = Factory.CreateOrganizationService(null);
        }

        public IServiceProvider ServiceProvider { get; }
        public IPluginExecutionContext ExecutionContext { get; }
        public ITracingService Tracing { get; }
        public IOrganizationServiceFactory Factory { get; }

        /// <summary>Runs as the calling user (respects security).</summary>
        public IOrganizationService UserService { get; }

        /// <summary>Runs as SYSTEM. Use only after an explicit access check.</summary>
        public IOrganizationService SystemService { get; }

        public void Trace(string format, params object[] args)
        {
            Tracing?.Trace(format, args);
        }

        public Entity Target =>
            ExecutionContext.InputParameters.Contains("Target") ? ExecutionContext.InputParameters["Target"] as Entity : null;

        public Entity PreImage(string name = "PreImage") =>
            ExecutionContext.PreEntityImages.Contains(name) ? ExecutionContext.PreEntityImages[name] : null;

        public Entity PostImage(string name = "PostImage") =>
            ExecutionContext.PostEntityImages.Contains(name) ? ExecutionContext.PostEntityImages[name] : null;
    }

    /// <summary>Common plumbing: tracing, timing and error wrapping for every CaseDesk plugin.</summary>
    public abstract class PluginBase : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var local = new LocalPluginContext(serviceProvider);
            var name = GetType().Name;
            var ctx = local.ExecutionContext;
            var watch = Stopwatch.StartNew();
            local.Trace("{0}: start {1} {2} stage {3} depth {4} correlation {5}",
                name, ctx.MessageName, ctx.PrimaryEntityName, ctx.Stage, ctx.Depth, ctx.CorrelationId);
            try
            {
                ExecuteCaseDesk(local);
            }
            catch (InvalidPluginExecutionException ex)
            {
                local.Trace("{0}: business rule stopped the operation: {1}", name, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                local.Trace("{0}: unexpected error: {1}", name, ex);
                throw new InvalidPluginExecutionException($"CaseDesk: an unexpected error occurred in {name}. Details are in the plug-in trace log.", ex);
            }
            finally
            {
                local.Trace("{0}: end after {1} ms", name, watch.ElapsedMilliseconds);
            }
        }

        protected abstract void ExecuteCaseDesk(LocalPluginContext context);
    }
}
