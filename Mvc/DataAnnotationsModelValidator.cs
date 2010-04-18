/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. All rights reserved.
 *
 * This software is subject to the Microsoft Public License (Ms-PL). 
 * A copy of the license can be found in the license.htm file included 
 * in this distribution.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

namespace System.Web.Mvc {
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;

    public class DataAnnotationsModelValidator : ModelValidator {
        public DataAnnotationsModelValidator(ModelMetadata metadata, ControllerContext context, ValidationAttribute attribute)
            : base(metadata, context) {

            if (attribute == null) {
                throw new ArgumentNullException("attribute");
            }

            Attribute = attribute;
        }

        protected internal ValidationAttribute Attribute { get; private set; }

        protected internal string ErrorMessage {
            get {
                string res;
                try
                {
                    // FormatErrorMessage might not be implemented. If not, then simply throw a generic validation error
                    res = Attribute.FormatErrorMessage(Metadata.GetDisplayName()); 
                }
                catch (NotImplementedException)
                {
                    res = "Validation error (" + Attribute.GetType().ToString() + "): " + Metadata.GetDisplayName();
                }
                return res;
            }
        }

        public override bool IsRequired {
            get {
                return Attribute is RequiredAttribute;
            }
        }

        internal static ModelValidator Create(ModelMetadata metadata, ControllerContext context, ValidationAttribute attribute) {
            return new DataAnnotationsModelValidator(metadata, context, attribute);
        }

        public override IEnumerable<ModelValidationResult> Validate(object container) {
            // RequiredAttribute is not implemented in mono. Do some checks, and
            // if there is still a NotImplementedException then try to handle it.
            string Message = "";
            try
            {
                if (!Attribute.IsValid(Metadata.Model))
                {
                    Message = ErrorMessage;
                }
            }
            catch (NotImplementedException)
            {
                if (IsRequired)
                {
                    if (ReferenceEquals(Metadata.Model, null))
                    {
                        Message = ErrorMessage + " is null";
                    }
                    else if (Metadata.Model.ToString() == "")
                    {
                        Message = ErrorMessage + " is empty";
                    }
                }
                else
                {
                    // We can't do anything, leave as if there is no validation error
                    Message = "";
                }
            }
            if (Message != "")
            {
                yield return new ModelValidationResult
                {
                    Message = ErrorMessage
                };
            }
        }
    }
}

