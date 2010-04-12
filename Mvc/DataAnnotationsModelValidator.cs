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
                // FormatErrorMessage is not implemented in mono
                return "Validation error (" + Attribute.GetType().ToString() + "): " + Metadata.GetDisplayName();
                //return Attribute.FormatErrorMessage(Metadata.GetDisplayName());
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
            // RequiredAttribute is not implemented in mono
            // This is a small workaround that might work for some and might break for a lot of objects
            if (IsRequired) {
              if (ReferenceEquals(Metadata.Model,null)) {
                yield return new ModelValidationResult
                {
                  Message = ErrorMessage + " is null"
                };
              } else if (Metadata.Model.ToString() == "") {
                yield return new ModelValidationResult
                {
                  Message = ErrorMessage + " is empty"
                };
              }
            } else
            if (!Attribute.IsValid(Metadata.Model)) {
                yield return new ModelValidationResult {
                    Message = ErrorMessage
                };
            }
        }
    }
}

