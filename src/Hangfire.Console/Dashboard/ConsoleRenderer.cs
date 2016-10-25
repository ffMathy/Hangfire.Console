﻿using Hangfire.Common;
using Hangfire.Console.Serialization;
using Hangfire.Dashboard;
using Hangfire.States;
using Hangfire.Storage;
using System;
using System.Collections.Generic;
using System.Text;

namespace Hangfire.Console.Dashboard
{
    /// <summary>
    /// Helper methods to render console shared between 
    /// <see cref="ProcessingStateRenderer"/> and <see cref="ConsoleDispatcher"/>.
    /// </summary>
    internal static class ConsoleRenderer
    {
        private static readonly HtmlHelper Helper = new HtmlHelper(new DummyPage());

        private class DummyPage : RazorPage
        {
            public override void Execute()
            {
            }
        }

        /// <summary>
        /// Renders a single <see cref="ConsoleLine"/> to a buffer.
        /// </summary>
        /// <param name="builder">Buffer</param>
        /// <param name="line">Line</param>
        /// <param name="timestamp">Reference timestamp for time offset</param>
        public static void RenderLine(StringBuilder builder, ConsoleLine line, DateTime timestamp)
        {
            var offset = TimeSpan.FromSeconds(line.TimeOffset);

            builder.Append("<div class=\"line\"");

            if (!string.IsNullOrEmpty(line.TextColor))
                builder.AppendFormat(" style=\"color:{0}\"", line.TextColor);

            builder.Append(">")
                   .Append(Helper.MomentTitle(timestamp + offset, Helper.ToHumanDuration(offset)))
                   .Append(Helper.HtmlEncode(line.Message))
                   .Append("</div>");
        }

        /// <summary>
        /// Renders a collection of <seealso cref="ConsoleLine"/> to a buffer.
        /// </summary>
        /// <param name="builder">Buffer</param>
        /// <param name="lines">Lines</param>
        /// <param name="timestamp">Reference timestamp for time offset</param>
        public static void RenderLines(StringBuilder builder, IEnumerable<ConsoleLine> lines, DateTime timestamp)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            if (lines == null) return;

            foreach (var line in lines)
            {
                RenderLine(builder, line, timestamp);
            }
        }
        
        /// <summary>
        /// Fetches and renders console contents to a buffer.
        /// </summary>
        /// <param name="builder">Buffer</param>
        /// <param name="storage">Job storage</param>
        /// <param name="consoleId">Console identifier</param>
        /// <param name="start">Offset to read lines from</param>
        public static void RenderConsole(StringBuilder builder, JobStorage storage, ConsoleId consoleId, int start)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));
            if (consoleId == null)
                throw new ArgumentNullException(nameof(consoleId));

            var items = ReadLines(storage, consoleId, ref start);

            builder.AppendFormat("<div class=\"console\" data-id=\"{0}\" data-n=\"{1}\">", consoleId, start);
            RenderLines(builder, items, consoleId.Timestamp);
            builder.Append("</div>");
        }

        /// <summary>
        /// Fetches console lines from storage.
        /// </summary>
        /// <param name="storage">Job storage</param>
        /// <param name="consoleId">Console identifier</param>
        /// <param name="start">Offset to read lines from</param>
        /// <remarks>
        /// On completion, <paramref name="start"/> is set to the end of the current batch, 
        /// and can be used for next requests (or set to -1, if the job has finished processing). 
        /// </remarks>
        private static IEnumerable<ConsoleLine> ReadLines(JobStorage storage, ConsoleId consoleId, ref int start)
        {
            if (start < 0) return null;

            using (var connection = (JobStorageConnection)storage.GetConnection())
            {
                var count = (int)connection.GetSetCount(consoleId.ToString());
                var result = new List<ConsoleLine>(Math.Max(1, count - start));

                if (count > start)
                {
                    // has some new items to fetch

                    var items = connection.GetRangeFromSet(consoleId.ToString(), start, count);
                    foreach (var item in items)
                    {
                        var entry = JobHelper.FromJson<ConsoleLine>(item);
                        result.Add(entry);
                    }
                }
                
                if (count <= start || start == 0)
                {
                    // no new items or initial load, check if the job is still performing
                    
                    var state = connection.GetStateData(consoleId.JobId);
                    if (state == null)
                    {
                        // No state found for a job, probably it was deleted
                        count = -2;
                    }
                    else if (!string.Equals(state.Name, ProcessingState.StateName, StringComparison.OrdinalIgnoreCase) ||
                             JobHelper.DeserializeDateTime(state.Data["StartedAt"]) != consoleId.Timestamp)
                    {
                        // Job has changed its state
                        count = -1;
                    }
                }
                
                start = count;
                return result;
            }
        }
    }
}
