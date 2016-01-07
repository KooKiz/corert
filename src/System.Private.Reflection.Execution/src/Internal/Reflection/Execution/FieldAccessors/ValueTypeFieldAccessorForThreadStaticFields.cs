// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using global::System;
using global::System.Threading;
using global::System.Reflection;
using global::System.Diagnostics;
using global::System.Collections.Generic;

using global::Internal.Runtime.Augments;
using global::Internal.Reflection.Execution;
using global::Internal.Reflection.Core.Execution;

using global::Internal.Metadata.NativeFormat;

using TargetException = System.ArgumentException;

namespace Internal.Reflection.Execution.FieldAccessors
{
    internal sealed class ValueTypeFieldAccessorForThreadStaticFields : StaticFieldAccessor
    {
        IntPtr _cookie;
        RuntimeTypeHandle _declaringTypeHandle;

        public ValueTypeFieldAccessorForThreadStaticFields(IntPtr cctorContext, RuntimeTypeHandle declaringTypeHandle, IntPtr cookie, RuntimeTypeHandle fieldTypeHandle)
            : base(cctorContext, fieldTypeHandle)
        {
            _cookie = cookie;
            _declaringTypeHandle = declaringTypeHandle;
        }

        protected sealed override Object GetFieldBypassCctor(Object obj)
        {
            IntPtr fieldAddress = RuntimeAugments.GetThreadStaticFieldAddress(_declaringTypeHandle, _cookie);
            return RuntimeAugments.LoadValueTypeField(fieldAddress, FieldTypeHandle);
        }

        protected sealed override void SetFieldBypassCctor(Object obj, Object value)
        {
            value = RuntimeAugments.CheckArgument(value, FieldTypeHandle);
            IntPtr fieldAddress = RuntimeAugments.GetThreadStaticFieldAddress(_declaringTypeHandle, _cookie);
            RuntimeAugments.StoreValueTypeField(fieldAddress, value, FieldTypeHandle);
        }
    }
}
