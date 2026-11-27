namespace DroidDeck.Integrations
{
    public interface IIntegration
    {
        string Name { get; }

        string Title { get; }

        bool IsConnected { get; }

        string? AccountName => null;
    }
}
