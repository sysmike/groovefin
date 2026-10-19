using System.Globalization;
using Groovefin.Core.Api;
using Groovefin.Core.Services;
using Microsoft.Extensions.Logging;

namespace Groovefin.Core.Notifications;

/// <summary>
/// <see cref="IErrorReporter"/> that turns exceptions into short, localized InfoBar messages and logs
/// the details.
/// </summary>
public sealed partial class ErrorReporter(
    INotificationService notifications,
    IStringResources strings,
    ILogger<ErrorReporter> logger) : IErrorReporter
{
    public void Report(Exception exception, string operationKey)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);

        if (exception is OperationCanceledException)
        {
            return;
        }

        var (messageKey, expected) = Classify(exception);
        if (expected)
        {
            LogExpected(exception, operationKey);
        }
        else
        {
            LogUnexpected(exception, operationKey);
        }

        var message = strings.GetString(messageKey);
        if (exception is JellyfinException { StatusCode: { } status })
        {
            // Not string.Format: a translation with stray braces must not make error reporting throw.
            message = message.Replace("{0}", ((int)status).ToString(CultureInfo.CurrentCulture), StringComparison.Ordinal);
        }

        notifications.Show(new AppNotification(NotificationSeverity.Error, strings.GetString(operationKey), message));
    }

    private static (string MessageKey, bool Expected) Classify(Exception exception) => exception switch
    {
        JellyfinException { IsUnauthorized: true } => ("Error_Unauthorized", true),
        JellyfinException { StatusCode: null } => ("Error_Unreachable", true),
        JellyfinException => ("Error_Server", true),
        IOException or UnauthorizedAccessException => ("Error_Storage", true),
        _ => ("Error_Unexpected", false),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Operation} failed")]
    private partial void LogExpected(Exception exception, string operation);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Operation} failed unexpectedly")]
    private partial void LogUnexpected(Exception exception, string operation);
}
