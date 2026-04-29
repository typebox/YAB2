using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Yab.Runtime
{
    public static class YabContext
    {
        private static readonly AsyncLocal<ImmutableStack<string>> _testIdStack = new();
        private static readonly AsyncLocal<string?> _currentTraceId = new();

        public static ImmutableStack<string> TestIdStack
        {
            get => _testIdStack.Value ?? ImmutableStack<string>.Empty;
            set => _testIdStack.Value = value;
        }

        public static string? CurrentTestId
        {
            get => TestIdStack.IsEmpty ? null : TestIdStack.Peek();
        }

        public static IEnumerable<string> AllCurrentTestIds => TestIdStack;

        public static string? CurrentTraceId
        {
            get => _currentTraceId.Value;
            set => _currentTraceId.Value = value;
        }

        public static void Clear()
        {
            _testIdStack.Value = ImmutableStack<string>.Empty;
            _currentTraceId.Value = null;
        }
    }
}
