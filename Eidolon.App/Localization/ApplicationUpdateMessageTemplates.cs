using Eidolon.App.Services;

namespace Eidolon.App.Localization
{
    public static class ApplicationUpdateMessageTemplates
    {
        public static string GetName(ApplicationUpdateMessageCode code)
        {
            switch (code)
            {
                case ApplicationUpdateMessageCode.DeploymentRequired:
                    return "EidolonText678";
                case ApplicationUpdateMessageCode.CurrentVersionInvalid:
                    return "EidolonText679";
                case ApplicationUpdateMessageCode.QueryFailed:
                    return "EidolonText680";
                case ApplicationUpdateMessageCode.ReleaseInvalid:
                    return "EidolonText681";
                case ApplicationUpdateMessageCode.DigestMissing:
                    return "EidolonText682";
                case ApplicationUpdateMessageCode.QueryUnavailable:
                    return "EidolonText683";
                case ApplicationUpdateMessageCode.AssetUnavailable:
                    return "EidolonText684";
                case ApplicationUpdateMessageCode.DownloadIncomplete:
                    return "EidolonText685";
                case ApplicationUpdateMessageCode.DownloadUnavailable:
                    return "EidolonText686";
                case ApplicationUpdateMessageCode.DigestMismatch:
                    return "EidolonText687";
                case ApplicationUpdateMessageCode.PrepareFailed:
                    return "EidolonText688";
                case ApplicationUpdateMessageCode.ReplaceFailed:
                    return "EidolonText689";
                case ApplicationUpdateMessageCode.RollbackFailed:
                    return "EidolonText690";
                case ApplicationUpdateMessageCode.CloseUnavailable:
                    return "EidolonText691";
                case ApplicationUpdateMessageCode.DownloadRequestFailed:
                    return "EidolonText692";
                case ApplicationUpdateMessageCode.LaunchFailed:
                    return "EidolonText693";
                case ApplicationUpdateMessageCode.WorkPending:
                    return "EidolonText694";
                case ApplicationUpdateMessageCode.Downloading:
                    return "EidolonText695";
                case ApplicationUpdateMessageCode.Preparing:
                    return "EidolonText696";
                case ApplicationUpdateMessageCode.ReadyToRestart:
                    return "EidolonText697";
                case ApplicationUpdateMessageCode.Cancelled:
                    return "EidolonText698";
                default:
                    throw new ArgumentOutOfRangeException(nameof(code), code, null);
            }
        }
    }
}
