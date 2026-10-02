using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.XPath;

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

    /// <summary>Returns the text content of the first node matching an XPath expression.</summary>
    public string? SelectXPath(string expression) =>
        _document.DocumentElement?.SelectSingleNode(expression)?.TextContent;

    /// <summary>Returns the serialized HTML document.</summary>
    public string OuterHtml => _document.DocumentElement?.OuterHtml ?? string.Empty;

    /// <summary>Returns the document text with markup removed.</summary>
    public string TextContent => _document.DocumentElement?.TextContent ?? string.Empty;

    public bool Exists(string selector) => _document.QuerySelector(selector) is not null;
}
