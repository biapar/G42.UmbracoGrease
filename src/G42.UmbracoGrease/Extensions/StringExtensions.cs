﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security;
using System.Web;
using System.Web.Mvc;
using G42.UmbracoGrease.Helpers;
using G42.UmbracoGrease.Models;
using HtmlAgilityPack;
using Umbraco.Web;

namespace G42.UmbracoGrease.Extensions
{
    public static class StringExtensions
    {
        public static string TruncateAtWord(this string text, int maxCharacters, string trailingStringIfTextCut = "…")
        {
            if (text == null || (text = text.Trim()).Length <= maxCharacters)
                return text;

            var trailLength = trailingStringIfTextCut.StartsWith("&") ? 1 : trailingStringIfTextCut.Length;
            maxCharacters = maxCharacters - trailLength >= 0 ? maxCharacters - trailLength : 0;
            var pos = text.LastIndexOf(" ", maxCharacters);
            if (pos >= 0)
                return text.Substring(0, pos) + trailingStringIfTextCut;

            return string.Empty;
        }

        public static string HighlightKeywords(this string text, string query)
        {
            var keywords = query.Split(' ');

            foreach (var keyword in keywords)
            {
                var regex = new Regex(keyword, RegexOptions.IgnoreCase);

                foreach (Match match in regex.Matches(text))
                {
                    text = text.Replace(match.Value, "<strong>" + match.Value + "</strong>");
                }
            }

            return text;
        }

        public static string ToXmlSafeString(this string input)
        {
            var escapedString = SecurityElement.Escape(input);

            return input == escapedString ? input : "<![CDATA[" + input + "]]>";
        }

        public static string AddOrdinal(this string input)
        {
            var num = Convert.ToInt32(input);

            if (num <= 0) return num.ToString();

            switch (num % 100)
            {
                case 11:
                case 12:
                case 13:
                    return input + "th";
            }

            switch (num % 10)
            {
                case 1:
                    return input + "st";
                case 2:
                    return input + "nd";
                case 3:
                    return input + "rd";
                default:
                    return input + "th";
            }
        }

        public static string ToHttpsUrl(this string input)
        {
            return input.Replace("http:", "https:");
        }

        public static string ToAzureBlobUrl(this string input)
        {
            var domain = HttpContext.Current.Request.Url.Host;

            if (domain.EndsWith(".local"))
                return input;

            return input.Replace("://", string.Format("://{0}/remote.axd/", domain));
        }

        public static int ToIntFromDoubleString(this string input)
        {
            return Convert.ToInt32(Math.Truncate(Convert.ToDouble(input)));
        }

        public static IHtmlString TransformImages(this string input, ControllerContext context)
        {
            if (String.IsNullOrWhiteSpace(input))
            {
                return new HtmlString(input);
            }

            var document = new HtmlDocument();
            document.LoadHtml(input);

            var spans = document.DocumentNode.SelectNodes("//span");
            if (spans == null)
                return new HtmlString(input);

            foreach (var span in spans)
            {
                var classAttribute = span.Attributes["class"];

                if (classAttribute != null && !String.IsNullOrWhiteSpace(classAttribute.Value))
                {
                    var spanClasses = classAttribute.Value.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);

                    var classesToTransform = ConfigurationManager.AppSettings["inlineImageTriggers"].Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);
                    var classValue = classAttribute.Value.Trim();

                    if (classesToTransform.Intersect(spanClasses).Any())
                    {
                        var img = span.Descendants().FirstOrDefault(x => x.Name == "img");

                        if (img != null)
                        {
                            var renderedTemplate = "";

                            //remove style attr
                            var styleAttr = img.Attributes["style"];
                            if (styleAttr != null)
                            {
                                styleAttr.Value = "";
                            }

                            var relAttr = img.Attributes["rel"];

                            var classAttr = img.Attributes["class"];

                            var umbHelper = new UmbracoHelper(UmbracoContext.Current);

                            var cropUrl = img.Attributes["src"].Value;

                            //for some reason the httputility decoders won't do this
                            cropUrl = cropUrl.Replace("&amp;", "&");

                            if (relAttr != null)
                            {
                                var queryString = cropUrl.Substring(cropUrl.IndexOf('?') + 1);

                                //LogHelper.Info<Uri>(queryString);

                                var parameterDictionary = new Dictionary<string, string>();

                                var pairs = queryString.Split(new[] { "&" }, StringSplitOptions.RemoveEmptyEntries);

                                foreach (var pair in pairs)
                                {
                                    var kvp = pair.Split(new[] { "=" }, StringSplitOptions.RemoveEmptyEntries);

                                    parameterDictionary.Add(kvp[0], kvp[1].Replace("px", ""));
                                }

                                cropUrl = umbHelper.TypedMedia(relAttr.Value).GetCropUrl(
                                    width: parameterDictionary["width"].ToIntFromDoubleString(),
                                    height: parameterDictionary["height"].ToIntFromDoubleString()
                                ).ToAzureBlobUrl();
                            }

                            var inlineStyle = new InlineStyle()
                            {
                                Type = classValue,
                                Text = span.InnerText,
                                Html = span.InnerHtml,
                                Meta = new ImageTag()
                                {
                                    Src = cropUrl,
                                    Title = img.Attributes["alt"].Value,
                                    Alt = img.Attributes["alt"].Value,
                                    Classes = (classAttr != null) ? classAttr.Value : "",
                                    Rel = (relAttr != null) ? relAttr.Value : ""
                                }
                            };

                            //TODO: change to appsetting
                            renderedTemplate =
                                TransformationHelper.RenderRazorViewToString(context, "~/Views/Partials/Shared/InlineStyles.cshtml",
                                    inlineStyle).Trim();

                            var newNode = HtmlNode.CreateNode(renderedTemplate);
                            span.ParentNode.ReplaceChild(newNode, span);
                        }
                    }
                }
            }

            return new HtmlString(document.DocumentNode.OuterHtml);
        }
    }
}