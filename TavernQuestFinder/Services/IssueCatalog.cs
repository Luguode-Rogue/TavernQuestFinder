using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using TavernQuestFinder.Logging;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.Localization;

namespace TavernQuestFinder.Services
{
    internal static class IssueCatalog
    {
        private static readonly Regex PascalCaseSplitter =
            new Regex("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);

        private static readonly Regex UnboundTextVariable =
            new Regex("\\{[A-Za-z0-9_.]+\\}", RegexOptions.Compiled);

        private static readonly OpCode[] OneByteOpCodes = new OpCode[0x100];
        private static readonly OpCode[] TwoByteOpCodes = new OpCode[0x100];

        private static readonly object DisplayNameCacheSync = new object();

        private static readonly Dictionary<Type, string> DisplayNameCache =
            new Dictionary<Type, string>();

        static IssueCatalog()
        {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(
                         BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is not OpCode opCode)
                {
                    continue;
                }

                ushort value = unchecked((ushort)opCode.Value);

                if (value < 0x100)
                {
                    OneByteOpCodes[value] = opCode;
                }
                else if ((value & 0xFF00) == 0xFE00)
                {
                    TwoByteOpCodes[value & 0xFF] = opCode;
                }
            }
        }

        public static IReadOnlyList<Type> GetAllIssueTypes()
        {
            var result = new List<Type>();
            int scannedAssemblies = 0;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                scannedAssemblies++;

                foreach (Type type in GetLoadableTypes(assembly))
                {
                    if (type.IsAbstract ||
                        type.IsGenericTypeDefinition ||
                        type == typeof(IssueBase) ||
                        !typeof(IssueBase).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    result.Add(type);
                }
            }

            List<Type> issueTypes = result
                .GroupBy(type =>
                    type.AssemblyQualifiedName ??
                    type.FullName ??
                    type.Name)
                .Select(group => group.First())
                .OrderBy(
                    GetDisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            TqfLog.Info(
                $"Issue catalog built | Assemblies={scannedAssemblies} " +
                $"| ConcreteIssueTypes={issueTypes.Count}");

            return issueTypes;
        }

        public static IReadOnlyList<Type> GetVanillaIssueTypes(
            IEnumerable<Type> issueTypes)
        {
            return issueTypes
                .Where(IssueCompatibilityService.IsTrustedVanillaIssue)
                .OrderBy(
                    GetDisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static IReadOnlyList<Type> GetExternalIssueTypes(
            IEnumerable<Type> issueTypes)
        {
            return issueTypes
                .Where(type =>
                    !IssueCompatibilityService.IsTrustedVanillaIssue(type))
                .OrderBy(
                    GetDisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static string GetDisplayName(Type issueType)
        {
            lock (DisplayNameCacheSync)
            {
                if (DisplayNameCache.TryGetValue(
                        issueType,
                        out string? cached))
                {
                    return cached;
                }
            }

            string? localizationTemplate =
                TryGetTitleLocalizationTemplate(issueType);

            string displayName;
            string source;

            if (!string.IsNullOrWhiteSpace(localizationTemplate))
            {
                try
                {
                    TextObject title =
                        CreateGenericTitleContext(
                            localizationTemplate);

                    string localized = title.ToString();

                    localized =
                        UnboundTextVariable.Replace(localized, "…");

                    localized = CollapseWhitespace(localized);

                    if (!string.IsNullOrWhiteSpace(localized))
                    {
                        displayName = localized;
                        source =
                            "Issue.Title TextObject + generic context";

                        CacheAndLogDisplayName(
                            issueType,
                            displayName,
                            source,
                            localizationTemplate);

                        return displayName;
                    }
                }
                catch (Exception exception)
                {
                    TqfLog.Warn(
                        $"Localized title resolution failed " +
                        $"| Type={issueType.FullName} " +
                        $"| Error={exception.GetType().Name}: {exception.Message}");
                }
            }

            string name = issueType.Name;

            if (name.EndsWith("Issue", StringComparison.Ordinal))
            {
                name = name.Substring(
                    0,
                    name.Length - "Issue".Length);
            }

            name = name.Replace('_', ' ');
            displayName = PascalCaseSplitter.Replace(name, " ");
            source = "ClassNameFallback";

            CacheAndLogDisplayName(
                issueType,
                displayName,
                source,
                localizationTemplate);

            return displayName;
        }

        private static TextObject CreateGenericTitleContext(
            string localizationTemplate)
        {
            var title =
                new TextObject(localizationTemplate);

            TextObject genericPerson =
                CreateGenericEntity(
                    "某人",
                    gender: 0);

            TextObject genericPlace =
                CreateGenericEntity("某地");

            TextObject genericClan =
                CreateGenericEntity("某家族");

            TextObject genericNoble =
                CreateGenericEntity(
                    "某位贵族",
                    gender: 0);

            // Character-like title variables.
            title.SetTextVariable(
                "ISSUE_GIVER",
                genericPerson);

            title.SetTextVariable(
                "ISSUE_OWNER",
                genericPerson);

            title.SetTextVariable(
                "QUEST_GIVER",
                genericPerson);

            title.SetTextVariable(
                "TARGET_HERO",
                genericPerson);

            // Settlement/location-like variables. Using a TextObject with both
            // a value and NAME/LINK attributes supports both {SETTLEMENT} and
            // {SETTLEMENT.NAME}-style localization strings.
            title.SetTextVariable(
                "ISSUE_GIVER_SETTLEMENT",
                genericPlace);

            title.SetTextVariable(
                "ISSUE_SETTLEMENT",
                genericPlace);

            title.SetTextVariable(
                "TARGET_SETTLEMENT",
                genericPlace);

            title.SetTextVariable(
                "SETTLEMENT",
                genericPlace);

            title.SetTextVariable(
                "VILLAGE",
                genericPlace);

            title.SetTextVariable(
                "TARGET_CITY",
                genericPlace);

            // Other title-specific dynamic values found in vanilla Issues.
            title.SetTextVariable(
                "CLAN_NAME",
                genericClan);

            title.SetTextVariable(
                "MALE_LESSER_NOBLE_NAME",
                genericNoble);

            // Prefer the ordinary horse branch for a generic menu title.
            title.SetTextVariable(
                "MOUNT_TYPE_IS_CAMEL",
                0);

            return title;
        }

        private static TextObject CreateGenericEntity(
            string text,
            int? gender = null)
        {
            var entity =
                new TextObject("{=!}" + text);

            entity.SetTextVariable(
                "NAME",
                new TextObject("{=!}" + text));

            entity.SetTextVariable(
                "LINK",
                new TextObject("{=!}" + text));

            entity.SetTextVariable(
                "FIRSTNAME",
                new TextObject("{=!}" + text));

            if (gender.HasValue)
            {
                entity.SetTextVariable(
                    "GENDER",
                    gender.Value);
            }

            return entity;
        }

        private static void CacheAndLogDisplayName(
            Type issueType,
            string displayName,
            string source,
            string? localizationTemplate)
        {
            lock (DisplayNameCacheSync)
            {
                DisplayNameCache[issueType] = displayName;
            }

            TqfLog.Info(
                $"Issue title resolved | Type={issueType.FullName} " +
                $"| Assembly={issueType.Assembly.GetName().Name} " +
                $"| Source={source} " +
                $"| Display=\"{displayName}\" " +
                $"| Template=\"{localizationTemplate ?? "<none>"}\"");
        }

        private static string? TryGetTitleLocalizationTemplate(
            Type issueType)
        {
            PropertyInfo? titleProperty = issueType.GetProperty(
                nameof(IssueBase.Title),
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            MethodInfo? getter = titleProperty?.GetGetMethod(true);
            MethodBody? methodBody = getter?.GetMethodBody();
            byte[]? il = methodBody?.GetILAsByteArray();

            if (getter == null || il == null || il.Length == 0)
            {
                return null;
            }

            int position = 0;

            try
            {
                while (position < il.Length)
                {
                    OpCode opCode;
                    byte first = il[position++];

                    if (first == 0xFE)
                    {
                        if (position >= il.Length)
                        {
                            return null;
                        }

                        opCode = TwoByteOpCodes[il[position++]];
                    }
                    else
                    {
                        opCode = OneByteOpCodes[first];
                    }

                    if (opCode.Equals(OpCodes.Ldstr))
                    {
                        if (position + 4 > il.Length)
                        {
                            return null;
                        }

                        int token = BitConverter.ToInt32(il, position);
                        position += 4;

                        string value = getter.Module.ResolveString(token);

                        if (value.StartsWith("{=", StringComparison.Ordinal))
                        {
                            return value;
                        }

                        continue;
                    }

                    position += GetOperandSize(
                        opCode,
                        il,
                        position);
                }
            }
            catch (Exception exception)
            {
                TqfLog.Warn(
                    $"Issue.Title IL inspection failed " +
                    $"| Type={issueType.FullName} " +
                    $"| Error={exception.GetType().Name}: {exception.Message}");
            }

            return null;
        }

        private static int GetOperandSize(
            OpCode opCode,
            byte[] il,
            int operandPosition)
        {
            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    return 0;

                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;

                case OperandType.InlineVar:
                    return 2;

                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;

                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;

                case OperandType.InlineSwitch:
                    if (operandPosition + 4 > il.Length)
                    {
                        return 0;
                    }

                    int count =
                        BitConverter.ToInt32(il, operandPosition);

                    return 4 + (count * 4);

                default:
                    return 0;
            }
        }

        private static string CollapseWhitespace(string value)
        {
            return Regex.Replace(value, "\\s+", " ").Trim();
        }

        private static IEnumerable<Type> GetLoadableTypes(
            Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                TqfLog.Warn(
                    $"Partial assembly type load " +
                    $"| Assembly={assembly.GetName().Name} " +
                    $"| LoaderErrors={exception.LoaderExceptions?.Length ?? 0}");

                return exception.Types
                    .Where(type => type != null)
                    .Cast<Type>();
            }
            catch (Exception exception)
            {
                TqfLog.Warn(
                    $"Assembly type scan skipped " +
                    $"| Assembly={assembly.GetName().Name} " +
                    $"| Error={exception.GetType().Name}: {exception.Message}");

                return Array.Empty<Type>();
            }
        }
    }
}
