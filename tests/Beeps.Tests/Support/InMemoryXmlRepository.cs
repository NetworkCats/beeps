using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Beeps.Tests.Support;

/// <summary>Keeps Data Protection keys in memory so tests never touch PostgreSQL.</summary>
internal sealed class InMemoryXmlRepository : IXmlRepository
{
    private readonly List<XElement> elements = [];
    private readonly Lock gate = new();

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (gate)
        {
            return [.. elements.Select(e => new XElement(e))];
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (gate)
        {
            elements.Add(new XElement(element));
        }
    }
}
