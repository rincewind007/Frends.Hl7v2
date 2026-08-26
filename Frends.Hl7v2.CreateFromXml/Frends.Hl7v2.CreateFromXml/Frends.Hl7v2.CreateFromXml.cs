using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using Frends.Hl7v2.CreateFromXml.Definitions;
using Frends.Hl7v2.CreateFromXml.Helpers;
using NHapi.Base.Model;
using NHapi.Base.Parser;
using NHapi.Base.Util;
using static Frends.Hl7v2.CreateFromXml.Definitions.Enums;

namespace Frends.Hl7v2.CreateFromXml;

/// <summary>
/// Task Class for Hl7v2 operations.
/// </summary>
public static class Hl7v2
{
    /// <summary>
    /// Task to create Hl7v2 message from Xml
    /// [Documentation](https://tasks.frends.com/tasks/frends-tasks/Frends-Hl7v2-CreateFromXml)
    /// </summary>
    /// <param name="input">Essential parameters.</param>
    /// <param name="options">Additional parameters.</param>
    /// <param name="cancellationToken">A cancellation token provided by Frends Platform.</param>
    /// <returns>object { bool Success, string Hl7v2Message, object Error { string Message, Exception AdditionalInfo } }</returns>
    public static Result CreateFromXml(
        [PropertyTab] Input input,
        [PropertyTab] Options options,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(input.Xml))
                throw new ArgumentNullException(nameof(input), "You must provide an XML.");
            var xmlParser = new DefaultXMLParser();
            var parserOptions = new ParserOptions
            {
                // nhapi's own default is to correct/collapse whitespace, so "disable trimming" is
                // the inverse of our CorrectWhitespaces switch.
                DisableWhitespaceTrimmingOnAllXmlNodes = !options.CorrectWhitespaces,

                // nhapi's own default is to silently add unrecognized tags inline (AddInline).
                UnexpectedSegmentBehaviour = options.CrashOnUnknownTags
                    ? UnexpectedSegmentBehaviour.ThrowHl7Exception
                    : UnexpectedSegmentBehaviour.AddInline,
            };
            var parsedMessage = xmlParser.Parse(input.Xml, parserOptions);

            // Validated against the pristine parse, before any MshOverrides are applied - otherwise every
            // field an override intentionally changes would look like a "missing" value.
            if (options.CrashOnDataLoss)
            {
                var roundTrippedXml = xmlParser.EncodeDocument(parsedMessage, parserOptions);
                DataLossValidator.ThrowIfDataWasLost(input.Xml, roundTrippedXml, options.CorrectWhitespaces);
            }

            var pipeParser = new PipeParser();
            var lineEnding = options.LineEnding switch
            {
                LineEnding.CRLF => "\r\n",
                LineEnding.LF => "\n",
                LineEnding.CR => "\r",
                _ => throw new ArgumentOutOfRangeException(nameof(options), "options.LineEnding is not valid."),
            };

            if (input.MshOverrides?.Length > 0)
                MshHelper.ApplyOverrides(parsedMessage, input.MshOverrides);

            var hl7Output = pipeParser.Encode(parsedMessage).ReplaceLineEndings(lineEnding);

            cancellationToken.ThrowIfCancellationRequested();

            return new Result
            {
                Success = true,
                Hl7v2Message = hl7Output,
            };
        }
        catch (Exception ex)
        {
            return ErrorHandler.Handle(ex, options.ThrowErrorOnFailure, options.ErrorMessageOnFailure);
        }
    }
}
