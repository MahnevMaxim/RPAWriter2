namespace RPAWriter.Core.AgentTools
{
    /// <summary>Metadata of one toggleable agent tool: real name, group, description and mutating flag.</summary>
    public sealed class AgentToolCatalogEntry
    {
        public AgentToolCatalogEntry(string name, string group, string description, bool mutates)
        {
            Name = name;
            Group = group;
            Description = description;
            Mutates = mutates;
        }

        public string Name { get; }
        public string Group { get; }
        public string Description { get; }
        public bool Mutates { get; }
    }
}