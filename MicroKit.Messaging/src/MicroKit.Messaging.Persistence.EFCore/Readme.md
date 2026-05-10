# Utilisation typique (Pattern Idempotence)
Voici comment tu utiliseras ce repository dans ton service de réception (ex: un Consumer Kafka/Rabbit) :
```chsarp
public async Task HandleIncomingMessage(RawMessage raw, string tenantId, string[] consumers)
{
    // 1. Vérifier si on l'a déjà vu
    if (await _repository.ExistsAsync(raw.Id)) return;

    // 2. Préparer le Root
    var inboxMessage = new InboxMessage
    {
        Id = raw.Id,
        MessageType = raw.Type,
        Payload = raw.Json,
        OccurredOnUtc = DateTimeOffset.UtcNow,
        Headers = raw.Headers,
        // 3. Créer un état pour chaque consommateur local
        InboxStates = consumers.Select(name => new InboxState
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConsumerName = name,
            Status = MessageStatus.Pending
        }).ToList()
    };

    // 4. Sauvegarder (Tout ou rien)
    await _repository.AddAsync(inboxMessage);
    await _unitOfWork.SaveChangesAsync();
}
```