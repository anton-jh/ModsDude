using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace ModsDude.Server.Api.Admin;

/// <summary>
/// What the last admin action said, carried across the redirect every successful or refused post
/// ends with, so refreshing the page never posts the form again. The layout shows it.
/// </summary>
public static class AdminFeedback
{
    private const string _errorKey = "AdminError";
    private const string _noticeKey = "AdminNotice";


    public static void SetError(this ITempDataDictionary tempData, string error)
    {
        tempData[_errorKey] = error;
    }

    public static void SetNotice(this ITempDataDictionary tempData, string notice)
    {
        tempData[_noticeKey] = notice;
    }

    public static string? TakeError(this ITempDataDictionary tempData)
    {
        return tempData[_errorKey] as string;
    }

    public static string? TakeNotice(this ITempDataDictionary tempData)
    {
        return tempData[_noticeKey] as string;
    }
}
