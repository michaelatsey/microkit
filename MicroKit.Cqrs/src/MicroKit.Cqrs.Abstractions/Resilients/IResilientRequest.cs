namespace MicroKit.Cqrs.Abstractions.Resilients
{
    public interface IResilientRequest
    {
        /// <summary>
        /// Le nom du pipeline de résilience à utiliser (ex: "SqlRetry", "ExternalApi").
        /// Si nul, le comportement utilisera la politique par défaut.
        /// </summary>
        string? PipelineName { get; }
    }
}
