using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Sanare.Core.Runtime.Documents;

/// <summary>AngleSharp-backed document boundary used by the v0.1 HTML runtime.</summary>
public sealed class HtmlDocument
{
    private readonly IDocument _document;

    public HtmlDocument(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _document = new HtmlParser().ParseDocument(content);
    }

    public string? SelectText(string selector) => _document.QuerySelector(selector)?.TextContent;

    public string? SelectAttribute(string selector, string attribute) =>
        _document.QuerySelector(selector)?.GetAttribute(attribute);

    public bool Exists(string selector) => _document.QuerySelector(selector) is not null;
}
