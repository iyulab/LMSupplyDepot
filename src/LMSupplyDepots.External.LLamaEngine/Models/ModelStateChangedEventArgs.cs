namespace LMSupplyDepots.External.LLamaEngine.Models;

public class ModelStateChangedEventArgs(
    string modelIdentifier,
    LocalModelState oldState,
    LocalModelState newState) : EventArgs
{
    public string ModelIdentifier { get; } = modelIdentifier;
    public LocalModelState OldState { get; } = oldState;
    public LocalModelState NewState { get; } = newState;
}