namespace MedicalApp.Common.Domain.Base.Interfaces;

public interface IAggregateRoot
{
    void AddDomainEvent(IEvent domainEvent);

    IReadOnlyList<IEvent> PopDomainEvents();
}