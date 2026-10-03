using System.Globalization;
using DataContainer.Generated;
using Dignus.DependencyInjection.Attributes;
using Dignus.Log;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;

namespace Eidolon.App.Localization
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class StringHelper
    {
        public AppLanguage Language { get; set; }
        public IEnumerable<string> Names
        {
            get
            {
                return TemplateContainer<StringTemplate>.Values.Select(template => template.Name);
            }
        }

        public StringHelper(StringLanguageSelection languageSelection)
        {
            Language = languageSelection.Language;
        }

        public string GetString(int id)
        {
            StringTemplate template = TemplateContainer<StringTemplate>.Find(id);
            if (template.Invalid() == true)
            {
                LogHelper.Error("Invalid string template. id:" + id + ".");
                return null;
            }
            return GetString(template);
        }

        public string GetString(string name)
        {
            StringTemplate template = TemplateContainer<StringTemplate>.Find(name);
            if (template.Invalid() == true)
            {
                LogHelper.Error("Invalid string template. name:" + name + ".");
                return null;
            }
            return GetString(template);
        }

        public string GetString(StringTemplate template)
        {
            if (template == null)
            {
                LogHelper.Fatal("String template is null.");
                return null;
            }
            if (template.Invalid() == true)
            {
                LogHelper.Error("Invalid string template. id:" + template.Id + ".");
                return null;
            }
            if (Language == AppLanguage.Korean)
            {
                return template.Kor;
            }
            if (Language == AppLanguage.English)
            {
                return template.Eng;
            }
            LogHelper.Error("Invalid string language. language:" + Language + ".");
            return null;
        }

        public string Format(string name, params object[] arguments)
        {
            string template = GetString(name);
            if (template == null)
            {
                return null;
            }
            CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
            if (Language == AppLanguage.Korean)
            {
                culture = CultureInfo.GetCultureInfo("ko-KR");
            }
            return string.Format(culture, template, arguments);
        }
        public string Format(StudioMessageCode code, params object[] arguments)
        {
            return Format(StudioMessageTemplates.GetName(code), arguments);
        }

        public string GetExceptionMessage(Exception error)
        {
            if (error is StudioException failure)
            {
                return Format(failure.Code, failure.Arguments);
            }
            return TranslateMessage(error.Message);
        }

        public string GetJobError(JobRecord job)
        {
            if (job.ErrorCode != StudioMessageCode.None)
            {
                return Format(job.ErrorCode, job.ErrorArguments);
            }
            return TranslateMessage(job.Error);
        }

        public string TranslateMessage(string message)
        {
            StringTemplate entry = TemplateContainer<StringTemplate>.Find(message);
            if (entry.Invalid() == false)
            {
                return GetString(entry);
            }
            foreach (StringTemplate template in TemplateContainer<StringTemplate>.Values)
            {
                if (template.Name.StartsWith("EidolonText", StringComparison.Ordinal) == true)
                {
                    if (template.Kor == message || template.Eng == message)
                    {
                        return GetString(template);
                    }
                }
            }
            foreach (StringTemplate template in TemplateContainer<StringTemplate>.Values)
            {
                if (template.Kor == message || template.Eng == message)
                {
                    return GetString(template);
                }
            }
            return message;
        }
    }
}
