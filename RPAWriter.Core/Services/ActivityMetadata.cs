using System;
using System.Collections.Generic;
using System.Reflection;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Reads activity argument metadata (display name, description, required flag,
    /// default value and data-flow direction) from reflected properties in a
    /// MetadataLoadContext-safe way: attribute data is read as raw
    /// <see cref="CustomAttributeData"/> because metadata types cannot instantiate
    /// attributes via <c>GetCustomAttributes()</c>.
    /// </summary>
    internal static class ActivityMetadata
    {
        public static IList<CustomAttributeData> ReadAttributes(PropertyInfo property)
        {
            if (property == null)
                return new List<CustomAttributeData>();

            try
            {
                return property.GetCustomAttributesData();
            }
            catch
            {
                return new List<CustomAttributeData>();
            }
        }

        public static IList<CustomAttributeData> ReadAttributes(Type type)
        {
            if (type == null)
                return new List<CustomAttributeData>();

            try
            {
                return type.GetCustomAttributesData();
            }
            catch
            {
                return new List<CustomAttributeData>();
            }
        }

        /// <summary>Reads the first constructor argument of an attribute as a string.</summary>
        public static string ReadStringAttribute(IList<CustomAttributeData> attributes, string attributeFullName)
        {
            foreach (var attribute in attributes ?? new List<CustomAttributeData>())
            {
                if (!string.Equals(attribute.AttributeType?.FullName, attributeFullName, StringComparison.Ordinal))
                    continue;

                var args = attribute.ConstructorArguments;
                if (args.Count == 0)
                    return null;

                var value = args[0].Value;
                return value?.ToString();
            }

            return null;
        }

        /// <summary>Reads the first constructor argument of a default-value attribute as a string.</summary>
        public static string ReadDefaultValue(IList<CustomAttributeData> attributes, string attributeFullName)
        {
            return ReadStringAttribute(attributes, attributeFullName);
        }

        /// <summary>True when the property carries the given attribute (presence-only, e.g. Required).</summary>
        public static bool HasAttribute(IList<CustomAttributeData> attributes, string attributeFullName)
        {
            foreach (var attribute in attributes ?? new List<CustomAttributeData>())
                if (string.Equals(attribute.AttributeType?.FullName, attributeFullName, StringComparison.Ordinal))
                    return true;

            return false;
        }

        /// <summary>
        /// Maps a WF argument wrapper type (<c>InArgument</c>/<c>OutArgument</c>/<c>InOutArgument</c>)
        /// to its data-flow direction, or "Property" for plain properties.
        /// </summary>
        public static string ExtractDirection(Type propertyType)
        {
            if (propertyType == null)
                return string.Empty;

            var name = propertyType.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
                name = name.Substring(0, tick);

            switch (name)
            {
                case "InArgument": return "In";
                case "OutArgument": return "Out";
                case "InOutArgument": return "InOut";
                default: return "Property";
            }
        }

        /// <summary>
        /// Returns the value type carried by a WF argument wrapper
        /// (<c>InArgument</c>/<c>OutArgument</c>/<c>InOutArgument</c>), or the property
        /// type itself for plain properties.
        /// </summary>
        public static Type ExtractValueType(Type propertyType)
        {
            if (propertyType == null || !propertyType.IsGenericType)
                return propertyType;

            var args = propertyType.GetGenericArguments();
            if (args.Length != 1)
                return propertyType;

            var name = propertyType.Name;
            var tick = name.IndexOf('`');
            if (tick >= 0)
                name = name.Substring(0, tick);

            switch (name)
            {
                case "InArgument":
                case "OutArgument":
                case "InOutArgument":
                    return args[0];
                default:
                    return propertyType;
            }
        }
    }
}
