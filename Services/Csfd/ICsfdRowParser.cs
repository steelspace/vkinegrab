using System;
using HtmlAgilityPack;
using vkinegrab.Models;

namespace vkinegrab.Services.Csfd;

public interface ICsfdRowParser
{
    Performance? Parse(HtmlNode row, DateOnly date, Uri requestUri);
}