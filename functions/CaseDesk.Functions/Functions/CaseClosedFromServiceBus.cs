using Azure.Messaging.ServiceBus;
using CaseDesk.Functions.CaseClosed;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CaseDesk.Functions.Functions;

/// <summary>
/// Default transport. Dataverse (service endpoint "CaseDesk CaseClosed ServiceBus", message format JSON)
/// puts the RemoteExecutionContext on queue <c>caseclosed</c>. Exceptions propagate so Service Bus
/// redelivers (max delivery count 10) and then dead-letters. Ignored/invalid messages complete normally.
/// </summary>
public sealed class CaseClosedFromServiceBus
{
    public const string Transport = "ServiceBus";

    private readonly CaseClosedHandler _handler;
    private readonly ILogger<CaseClosedFromServiceBus> _logger;

    public CaseClosedFromServiceBus(CaseClosedHandler handler, ILogger<CaseClosedFromServiceBus> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    [Function(nameof(CaseClosedFromServiceBus))]
    public async Task Run(
        [ServiceBusTrigger("caseclosed", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("CaseClosedMessageReceived {Transport} {ServiceBusMessageId} {DeliveryCount}",
            Transport, message.MessageId, message.DeliveryCount);

        var result = await _handler.HandleAsync(message.Body.ToString(), Transport, cancellationToken);

        if (result.Outcome != HandleOutcome.Forwarded)
        {
            // Not retryable: let the message complete instead of burning 10 deliveries on it.
            _logger.LogWarning("CaseClosedNotForwarded {Transport} {ServiceBusMessageId} {Outcome}: {Reason}",
                Transport, message.MessageId, result.Outcome, result.Reason);
        }
    }
}
