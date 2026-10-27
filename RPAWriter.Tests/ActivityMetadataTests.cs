using System;
using System.Reflection;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    // Test stand-ins: ActivityMetadata inspects only the type *name* (and its generic
    // argument), so local classes named like the WF wrappers are enough to exercise it.
    public class InArgument<T> { }
    public class OutArgument<T> { }
    public class InOutArgument<T> { }
    public class PlainPropertyType { }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class MarkerAttribute : Attribute
    {
        public MarkerAttribute(string value) { Value = value; }
        public string Value { get; }
    }

    public class SampleWithAttributes
    {
        [Marker("hello")]
        public string Decorated { get; set; }

        public string Plain { get; set; }
    }

    [TestFixture]
    public class ActivityMetadataTests
    {
        // ---------- ExtractDirection ----------

        [Test]
        public void ExtractDirection_MapsArgumentWrappers()
        {
            Assert.That(CallStatic("ExtractDirection", typeof(InArgument<int>)), Is.EqualTo("In"));
            Assert.That(CallStatic("ExtractDirection", typeof(OutArgument<int>)), Is.EqualTo("Out"));
            Assert.That(CallStatic("ExtractDirection", typeof(InOutArgument<int>)), Is.EqualTo("InOut"));
        }

        [Test]
        public void ExtractDirection_PlainProperty_ReturnsProperty()
        {
            Assert.That(CallStatic("ExtractDirection", typeof(PlainPropertyType)), Is.EqualTo("Property"));
        }

        [Test]
        public void ExtractDirection_Null_ReturnsEmpty()
        {
            Assert.That(CallStatic("ExtractDirection", new object[] { null }), Is.EqualTo(string.Empty));
        }

        // ---------- ExtractValueType ----------

        [Test]
        public void ExtractValueType_UnwrapsArgumentGeneric()
        {
            Assert.That(CallStatic("ExtractValueType", typeof(InArgument<string>)), Is.EqualTo(typeof(string)));
            Assert.That(CallStatic("ExtractValueType", typeof(OutArgument<int>)), Is.EqualTo(typeof(int)));
            Assert.That(CallStatic("ExtractValueType", typeof(InOutArgument<long>)), Is.EqualTo(typeof(long)));
        }

        [Test]
        public void ExtractValueType_NonArgumentGeneric_ReturnsTypeItself()
        {
            Assert.That(CallStatic("ExtractValueType", typeof(PlainPropertyType)), Is.EqualTo(typeof(PlainPropertyType)));
            Assert.That(CallStatic("ExtractValueType", typeof(string)), Is.EqualTo(typeof(string)));
        }

        [Test]
        public void ExtractValueType_Null_ReturnsNull()
        {
            Assert.That(CallStatic("ExtractValueType", new object[] { null }), Is.Null);
        }

        // ---------- HasAttribute / ReadStringAttribute ----------

        [Test]
        public void HasAttribute_ReturnsTrueWhenPresent()
        {
            var attrs = typeof(SampleWithAttributes).GetProperty("Decorated").GetCustomAttributesData();

            Assert.That(
                CallStatic("HasAttribute", attrs, typeof(MarkerAttribute).FullName),
                Is.EqualTo(true));
        }

        [Test]
        public void HasAttribute_ReturnsFalseWhenAbsent()
        {
            var attrs = typeof(SampleWithAttributes).GetProperty("Plain").GetCustomAttributesData();

            Assert.That(
                CallStatic("HasAttribute", attrs, typeof(MarkerAttribute).FullName),
                Is.EqualTo(false));
        }

        [Test]
        public void ReadStringAttribute_ReturnsFirstConstructorArgument()
        {
            var attrs = typeof(SampleWithAttributes).GetProperty("Decorated").GetCustomAttributesData();

            Assert.That(
                CallStatic("ReadStringAttribute", attrs, typeof(MarkerAttribute).FullName),
                Is.EqualTo("hello"));
        }

        [Test]
        public void ReadStringAttribute_NoMatchOrNull_ReturnsNull()
        {
            var empty = typeof(SampleWithAttributes).GetProperty("Plain").GetCustomAttributesData();

            Assert.That(CallStatic("ReadStringAttribute", empty, typeof(MarkerAttribute).FullName), Is.Null);
            Assert.That(CallStatic("ReadStringAttribute", null, typeof(MarkerAttribute).FullName), Is.Null);
        }

        // ---------- helpers ----------

        private static object CallStatic(string methodName, params object[] args)
        {
            var type = typeof(ActivityLibraryPaths).Assembly
                .GetType("RPAWriter.Core.Services.ActivityMetadata", throwOnError: true);
            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Static method not found: " + methodName);
            return method.Invoke(null, args);
        }
    }
}
