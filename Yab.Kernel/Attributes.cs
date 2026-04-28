using System;

namespace Yab.Attributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = true)]
    public class ConceptAttribute : Attribute
    {
        public string Name { get; }
        public ConceptAttribute(string name) => Name = name;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property)]
    public class IntentAttribute : Attribute
    {
        public string Description { get; }
        public IntentAttribute(string description) => Description = description;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class VerifiesAttribute : Attribute
    {
        public string Target { get; }
        public VerifiesAttribute(string target) => Target = target;
    }
}
