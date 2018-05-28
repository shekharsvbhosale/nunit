﻿// ***********************************************************************
// Copyright (c) 2018 Charlie Poole, Rob Prouse
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

using System;
using System.Reflection;
using NUnit.Compatibility;

namespace NUnit.Framework.Internal
{
    internal static partial class CSharpPatternBasedAwaitAdapter
    {
        private sealed class AwaitShapeInfo
        {
            private readonly MethodInfo _getAwaiterMethod;
            private readonly MethodInfo _isCompletedGetter;
            private readonly MethodInfo _onCompletedMethod;
            private readonly MethodInfo _getResultMethod;

            public AwaitShapeInfo(MethodInfo getAwaiterMethod, MethodInfo isCompletedGetter, MethodInfo onCompletedMethod, MethodInfo getResultMethod)
            {
                _getAwaiterMethod = getAwaiterMethod;
                _isCompletedGetter = isCompletedGetter;
                _onCompletedMethod = onCompletedMethod;
                _getResultMethod = getResultMethod;
            }

            public static AwaitShapeInfo TryCreate(Type awaitableType)
            {
                // See https://docs.microsoft.com/dotnet/csharp/language-reference/language-specification/expressions#awaitable-expressions
                // This section was first established in C# 5 and has not been updated as of C# 7.3.

                // Something we might consider doing is checking to see if the Microsoft.CSharp assembly is loadable 
                // and then driving it via reflection as though we were generating `return await ((dynamic)awaitable);`.
                // That would automatically opt into future changes to the spec, if any, but hardly seems worthwhile
                // since this code is needed as a fallback anyway.

                var getAwaiterMethod = awaitableType.GetNonGenericPublicInstanceMethod("GetAwaiter", Type.EmptyTypes);
                if (getAwaiterMethod == null || getAwaiterMethod.GetGenericArguments().Length != 0) return null;

                var awaiterType = getAwaiterMethod.ReturnType;
                var notifyCompletionInterface = awaiterType.GetTypeInfo().GetInterface("System.Runtime.CompilerServices.INotifyCompletion");
                if (notifyCompletionInterface == null) return null;
                var onCompletedMethod = notifyCompletionInterface.GetNonGenericPublicInstanceMethod("OnCompleted", new[] { typeof(Action) });
                if (onCompletedMethod == null) return null;

                var isCompletedProperty = awaiterType.GetPublicInstanceProperty("IsCompleted", Type.EmptyTypes);
                if (isCompletedProperty == null || isCompletedProperty.PropertyType != typeof(bool)) return null;
                var isCompletedGetter = isCompletedProperty.GetGetMethod();
                if (isCompletedGetter == null) return null;

                var getResultMethod = awaiterType.GetNonGenericPublicInstanceMethod("GetResult", Type.EmptyTypes);
                if (getResultMethod == null) return null;

                var criticalNotifyCompletionInterface = awaiterType.GetTypeInfo().GetInterface("System.Runtime.CompilerServices.ICriticalNotifyCompletion");
                var unsafeOnCompletedMethod = criticalNotifyCompletionInterface?.GetNonGenericPublicInstanceMethod("UnsafeOnCompleted", new[] { typeof(Action) });

                return new AwaitShapeInfo(
                    getAwaiterMethod,
                    isCompletedGetter,
                    unsafeOnCompletedMethod ?? onCompletedMethod,
                    getResultMethod);
            }

            public AwaitAdapter CreateAwaitAdapter(object awaitable)
            {
                // We could emit ideal implementations of AwaitAdapter, customized to each awaitable type.
                // But generating executable code at runtime is unsupported by design on some platforms.
                // By the same token, MakeGenericMethod is not supported for types not included at compile time.

                // So let’s start with a less efficient, reflection-based approach which works anywhere
                // and is much easier to follow, and add the complex thing only if we need to hit a perf goal.

                return new ReflectionAdapter(
                    _getAwaiterMethod.InvokeWithTransparentExceptions(awaitable),
                    _isCompletedGetter,
                    _onCompletedMethod,
                    _getResultMethod);
            }

            public Type ResultType => _getResultMethod.ReturnType;
        }
    }
}
