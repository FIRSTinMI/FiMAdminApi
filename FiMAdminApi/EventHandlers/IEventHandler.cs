namespace FiMAdminApi.EventHandlers;

public interface IEventHandler<in TEvent>
{
    public Task Handle(TEvent evt);
}