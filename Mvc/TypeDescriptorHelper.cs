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

namespace System.Web.Mvc
{
  using System;
  using System.Collections.Generic;
  using System.ComponentModel;
  using System.ComponentModel.DataAnnotations;
  using System.Globalization;
  using System.Linq;
  using System.Reflection;
  using System.Web.Mvc.Resources;
  using System.ComponentModel.Design;
  using System.Collections;

  internal static class TypeDescriptorHelper
  {

    private static Func<Type, ICustomTypeDescriptor> _typeDescriptorFactory = GetTypeDescriptorFactory();

    private static Func<Type, ICustomTypeDescriptor> GetTypeDescriptorFactory()
    {
      // MONO 2.4.4 fix: copy mono 2.6 version locally (the same way MS did with .NET 3.5)
      // MONO 2.6 fix: do not use MS .NET 4 fix
      //if (Environment.Version.Major < 4) {
      //    return type => new _AssociatedMetadataTypeTypeDescriptionProvider(type).GetTypeDescriptor(type);
      //}
      bool use_original = true;
      if (Type.GetType("Mono.Runtime") != null)
      {
        try {
          new AssociatedMetadataTypeTypeDescriptionProvider(typeof(System.Object));
        } 
        catch (NotImplementedException) {
          use_original = false;
        }
      }
      if (use_original) {
          return type => new AssociatedMetadataTypeTypeDescriptionProvider(type).GetTypeDescriptor(type);
      } else {
          return type => new _AssociatedMetadataTypeTypeDescriptionProvider(type).GetTypeDescriptor(type);
      }
      
    }

    public static ICustomTypeDescriptor Get(Type type)
    {
      return _typeDescriptorFactory(type);
    }

    // Backport classes from MONO 2.6.3 for MONO 2.4.4
    // Using only if AssociatedMetadataTypeTypeDescriptionProvider doesn't work

    #region _AssociatedMetadataTypeTypeDescriptionProvider

    public class _AssociatedMetadataTypeTypeDescriptionProvider : TypeDescriptionProvider
    {
      Type type;
      Type associatedMetadataType;

      public _AssociatedMetadataTypeTypeDescriptionProvider(Type type)
      {
        if (type == null)
          throw new ArgumentNullException("type");

        this.type = type;
      }

      public _AssociatedMetadataTypeTypeDescriptionProvider(Type type, Type associatedMetadataType)
      {
        if (type == null)
          throw new ArgumentNullException("type");
        if (associatedMetadataType == null)
          throw new ArgumentNullException("associatedMetadataType");

        this.type = type;
        this.associatedMetadataType = associatedMetadataType;
      }

      public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
      {
        return new _AssociatedMetadataTypeTypeDescriptor(base.GetTypeDescriptor(objectType, instance), type, associatedMetadataType);
      }
    }

    #endregion

    #region _AssociatedMetadataTypeTypeDescriptor

    class _AssociatedMetadataTypeTypeDescriptor : CustomTypeDescriptor
    {
      Type type;
      Type associatedMetadataType;
      bool associatedMetadataTypeChecked;
      PropertyDescriptorCollection properties;

      Type AssociatedMetadataType
      {
        get
        {
          if (!associatedMetadataTypeChecked && associatedMetadataType == null)
            associatedMetadataType = FindMetadataType();

          return associatedMetadataType;
        }
      }

      public _AssociatedMetadataTypeTypeDescriptor(ICustomTypeDescriptor parent, Type type)
        : this(parent, type, null)
      {
      }

      public _AssociatedMetadataTypeTypeDescriptor(ICustomTypeDescriptor parent, Type type, Type associatedMetadataType)
        : base(parent)
      {
        this.type = type;
        this.associatedMetadataType = associatedMetadataType;
      }

      void CopyAttributes(object[] from, List<Attribute> to)
      {
        foreach (object o in from)
        {
          Attribute a = o as Attribute;
          if (a == null)
            continue;

          to.Add(a);
        }
      }

      public override AttributeCollection GetAttributes()
      {
        var attributes = new List<Attribute>();
        CopyAttributes(type.GetCustomAttributes(true), attributes);

        Type metaType = AssociatedMetadataType;
        if (metaType != null)
          CopyAttributes(metaType.GetCustomAttributes(true), attributes);

        return new AttributeCollection(attributes.ToArray());
      }

      public override PropertyDescriptorCollection GetProperties()
      {
        // Code partially copied from TypeDescriptor.TypeInfo.GetProperties
        if (properties != null)
          return properties;

        Dictionary<string, MemberInfo> metaMembers = null;
        var propertiesHash = new Dictionary<string, bool>(); // name - null
        var propertiesList = new List<_AssociatedMetadataTypePropertyDescriptor>();
        Type currentType = type;
        Type metaType = AssociatedMetadataType;

        if (metaType != null)
        {
          metaMembers = new Dictionary<string, MemberInfo>();
          MemberInfo[] members = metaType.GetMembers(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

          foreach (MemberInfo member in members)
          {
            switch (member.MemberType)
            {
              case MemberTypes.Field:
              case MemberTypes.Property:
                break;

              default:
                continue;
            }

            string name = member.Name;
            if (metaMembers.ContainsKey(name))
              continue;

            metaMembers.Add(name, member);
          }
        }

        // Getting properties type by type, because in the case of a property in the child type, where
        // the "new" keyword is used and also the return type is changed Type.GetProperties returns 
        // also the parent property. 
        // 
        // Note that we also have to preserve the properties order here.
        // 
        while (currentType != null && currentType != typeof(object))
        {
          PropertyInfo[] props = currentType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
          foreach (PropertyInfo property in props)
          {
            string propName = property.Name;

            if (property.GetIndexParameters().Length == 0 && property.CanRead && !propertiesHash.ContainsKey(propName))
            {
              MemberInfo metaMember;

              if (metaMembers != null)
                metaMembers.TryGetValue(propName, out metaMember);
              else
                metaMember = null;
              propertiesList.Add(new _AssociatedMetadataTypePropertyDescriptor(property, metaMember));
              propertiesHash.Add(propName, true);
            }
          }
          currentType = currentType.BaseType;
        }

        properties = new PropertyDescriptorCollection((PropertyDescriptor[])propertiesList.ToArray(), true);
        return properties;
      }

      Type FindMetadataType()
      {
        associatedMetadataTypeChecked = true;
        if (type == null)
          return null;

        object[] attrs = type.GetCustomAttributes(typeof(MetadataTypeAttribute), true);
        if (attrs == null || attrs.Length == 0)
          return null;

        var attr = attrs[0] as MetadataTypeAttribute;
        if (attr == null)
          return null;

        return attr.MetadataClassType;
      }
    }

    #endregion

    #region _AssociatedMetadataTypePropertyDescriptor
    class _AssociatedMetadataTypePropertyDescriptor : _ReflectionPropertyDescriptor
    {
      MemberInfo metaTypeMember;

      public _AssociatedMetadataTypePropertyDescriptor(PropertyInfo typeProperty, MemberInfo metaTypeMember)
        : base(typeProperty)
      {
        this.metaTypeMember = metaTypeMember;
      }

      protected override void FillAttributes(IList attributeList)
      {
        base.FillAttributes(attributeList);
        if (metaTypeMember == null)
          return;

        object[] attributes = metaTypeMember.GetCustomAttributes(false);
        if (attributes == null || attributes.Length == 0)
          return;

        foreach (object o in attributes)
        {
          var attr = o as Attribute;
          if (attr == null)
            continue;

          attributeList.Add(attr);
        }
      }
    }

    #endregion

    #region _ReflectionPropertyDescriptor

    class _ReflectionPropertyDescriptor : PropertyDescriptor
    {
      PropertyInfo _member;
      Type _componentType;
      Type _propertyType;
      PropertyInfo getter, setter;
      bool accessors_inited;

      public _ReflectionPropertyDescriptor(Type componentType, PropertyDescriptor oldPropertyDescriptor, Attribute[] attributes)
        : base(oldPropertyDescriptor, attributes)
      {
        _componentType = componentType;
        _propertyType = oldPropertyDescriptor.PropertyType;
      }

      public _ReflectionPropertyDescriptor(Type componentType, string name, Type type, Attribute[] attributes)
        : base(name, attributes)
      {
        _componentType = componentType;
        _propertyType = type;
      }

      public _ReflectionPropertyDescriptor(PropertyInfo info)
        : base(info.Name, null)
      {
        _member = info;
        _componentType = _member.DeclaringType;
        _propertyType = info.PropertyType;
      }

      PropertyInfo GetPropertyInfo()
      {
        if (_member == null)
        {
          _member = _componentType.GetProperty(Name, BindingFlags.GetProperty | BindingFlags.NonPublic |
                        BindingFlags.Public | BindingFlags.Instance,
                        null, this.PropertyType,
                        new Type[0], new ParameterModifier[0]);
          if (_member == null)
            throw new ArgumentException("Accessor methods for the " + Name + " property are missing");
        }
        return _member;
      }

      public override Type ComponentType
      {
        get { return _componentType; }
      }

      public override bool IsReadOnly
      {
        get
        {
          ReadOnlyAttribute attrib = ((ReadOnlyAttribute)Attributes[typeof(ReadOnlyAttribute)]);
          return !GetPropertyInfo().CanWrite || attrib.IsReadOnly;
        }
      }

      public override Type PropertyType
      {
        get { return _propertyType; }
      }

      // The last added to the list attributes have higher precedence
      //
      protected override void FillAttributes(IList attributeList)
      {
        base.FillAttributes(attributeList);

        if (!GetPropertyInfo().CanWrite)
          attributeList.Add(ReadOnlyAttribute.Yes);

        // PropertyDescriptor merges the attributes of both virtual and also "new" properties 
        // in the the component type hierarchy.
        // 
        int numberOfBaseTypes = 0;
        Type baseType = this.ComponentType;
        while (baseType != null && baseType != typeof(object))
        {
          numberOfBaseTypes++;
          baseType = baseType.BaseType;
        }

        Attribute[][] hierarchyAttributes = new Attribute[numberOfBaseTypes][];
        baseType = this.ComponentType;
        while (baseType != null && baseType != typeof(object))
        {
          PropertyInfo property = baseType.GetProperty(Name, BindingFlags.NonPublic |
                          BindingFlags.Public | BindingFlags.Instance |
                          BindingFlags.DeclaredOnly,
                          null, this.PropertyType,
                          new Type[0], new ParameterModifier[0]);
          if (property != null)
          {
            object[] attrObjects = property.GetCustomAttributes(false);
            Attribute[] attrsArray = new Attribute[attrObjects.Length];
            attrObjects.CopyTo(attrsArray, 0);
            // add in reverse order so that the base types have lower precedence
            hierarchyAttributes[--numberOfBaseTypes] = attrsArray;
          }
          baseType = baseType.BaseType;
        }

        foreach (Attribute[] attrArray in hierarchyAttributes)
        {
          if (attrArray != null)
          {
            foreach (Attribute attr in attrArray)
              attributeList.Add(attr);
          }
        }

        foreach (Attribute attribute in TypeDescriptor.GetAttributes(PropertyType))
          attributeList.Add(attribute);
      }

#pragma warning disable 0618
      public override object GetValue(object component)
      {
        component = MemberDescriptor.GetInvokee(_componentType, component);
        InitAccessors();
        return getter.GetValue(component, null);
      }
#pragma warning restore 0618

      DesignerTransaction CreateTransaction(object obj, string description)
      {
        IComponent com = obj as IComponent;
        if (com == null || com.Site == null)
          return null;

        IDesignerHost dh = (IDesignerHost)com.Site.GetService(typeof(IDesignerHost));
        if (dh == null)
          return null;

        DesignerTransaction tran = dh.CreateTransaction(description);
        IComponentChangeService ccs = (IComponentChangeService)com.Site.GetService(typeof(IComponentChangeService));
        if (ccs != null)
          ccs.OnComponentChanging(com, this);
        return tran;
      }

      void EndTransaction(object obj, DesignerTransaction tran, object oldValue, object newValue, bool commit)
      {
        if (tran == null)
        {
          // FIXME: EventArgs might be differen type.
          OnValueChanged(obj, new PropertyChangedEventArgs(Name));
          return;
        }

        if (commit)
        {
          IComponent com = obj as IComponent;
          IComponentChangeService ccs = (IComponentChangeService)com.Site.GetService(typeof(IComponentChangeService));
          if (ccs != null)
            ccs.OnComponentChanged(com, this, oldValue, newValue);
          tran.Commit();
          // FIXME: EventArgs might be differen type.
          OnValueChanged(obj, new PropertyChangedEventArgs(Name));
        }
        else
          tran.Cancel();
      }

      /*
      This method exists because reflection is way too low level for what we need.
      A given virtual property that is partially overriden by a child won't show the
      non-overriden accessor in PropertyInfo. IOW:
      class Parent {
        public virtual string Prop { get; set; }
      }
      class Child : Parent {
        public override string Prop {
          get { return "child"; }
        }
      }
      PropertyInfo pi = typeof (Child).GetProperty ("Prop");
      pi.GetGetMethod (); //returns the MethodInfo for the overridden getter
      pi.GetSetMethod (); //returns null as no override exists
      */
      void InitAccessors()
      {
        if (accessors_inited)
          return;
        PropertyInfo prop = GetPropertyInfo();
        MethodInfo setterMethod, getterMethod;
        setterMethod = prop.GetSetMethod(true);
        getterMethod = prop.GetGetMethod(true);

        if (getterMethod != null)
          getter = prop;

        if (setterMethod != null)
          setter = prop;


        if (setterMethod != null && getterMethod != null)
        {//both exist
          accessors_inited = true;
          return;
        }
        if (setterMethod == null && getterMethod == null)
        {//neither exist, this is a broken property
          accessors_inited = true;
          return;
        }

        //In order to detect that this is a virtual property with override, we check the non null accessor
        MethodInfo mi = getterMethod != null ? getterMethod : setterMethod;

        if (mi == null || !mi.IsVirtual || (mi.Attributes & MethodAttributes.NewSlot) == MethodAttributes.NewSlot)
        {
          accessors_inited = true;
          return;
        }

        Type type = _componentType.BaseType;
        while (type != null && type != typeof(object))
        {
          prop = type.GetProperty(Name, BindingFlags.GetProperty | BindingFlags.NonPublic |
                            BindingFlags.Public | BindingFlags.Instance,
                            null, this.PropertyType,
                            new Type[0], new ParameterModifier[0]);
          if (prop == null) //nothing left to search
            break;
          if (setterMethod == null)
            setterMethod = mi = prop.GetSetMethod();
          else
            getterMethod = mi = prop.GetGetMethod();

          if (getterMethod != null && getter == null)
            getter = prop;

          if (setterMethod != null && setter == null)
            setter = prop;

          if (mi != null)
            break;
          type = type.BaseType;
        }
        accessors_inited = true;
      }

#pragma warning disable 0618
      public override void SetValue(object component, object value)
      {
        DesignerTransaction tran = CreateTransaction(component, "Set Property '" + Name + "'");

        object propertyHolder = MemberDescriptor.GetInvokee(_componentType, component);
        object old = GetValue(propertyHolder);

        try
        {
          InitAccessors();
          setter.SetValue(propertyHolder, value, null);
          EndTransaction(component, tran, old, value, true);
        }
        catch
        {
          EndTransaction(component, tran, old, value, false);
          throw;
        }
      }
#pragma warning restore 0618

      MethodInfo FindPropertyMethod(object o, string method_name)
      {
        MethodInfo mi = null;
        string name = method_name + Name;

        foreach (MethodInfo m in o.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
          // XXX should we really not check the return type of the method?
          if (m.Name == name && m.GetParameters().Length == 0)
          {
            mi = m;
            break;
          }
        }

        return mi;
      }

#pragma warning disable 0618
      public override void ResetValue(object component)
      {
        object propertyHolder = MemberDescriptor.GetInvokee(_componentType, component);

        DefaultValueAttribute attrib = ((DefaultValueAttribute)Attributes[typeof(DefaultValueAttribute)]);
        if (attrib != null)
          SetValue(propertyHolder, attrib.Value);

        DesignerTransaction tran = CreateTransaction(component, "Reset Property '" + Name + "'");
        object old = GetValue(propertyHolder);

        try
        {
          MethodInfo mi = FindPropertyMethod(propertyHolder, "Reset");
          if (mi != null)
            mi.Invoke(propertyHolder, null);
          EndTransaction(component, tran, old, GetValue(propertyHolder), true);
        }
        catch
        {
          EndTransaction(component, tran, old, GetValue(propertyHolder), false);
          throw;
        }
      }
#pragma warning restore 0618

#pragma warning disable 0618
      public override bool CanResetValue(object component)
      {
        component = MemberDescriptor.GetInvokee(_componentType, component);

        DefaultValueAttribute attrib = ((DefaultValueAttribute)Attributes[typeof(DefaultValueAttribute)]);
        if (attrib != null)
        {
          object current = GetValue(component);
          if (attrib.Value == null || current == null)
          {
            if (attrib.Value != current)
              return true;
            if (attrib.Value == null && current == null)
              return false;
          }

          return !attrib.Value.Equals(current);
        }
        else
        {
#if NET_2_0
				if (!_member.CanWrite)
					return false;
#endif
          MethodInfo mi = FindPropertyMethod(component, "ShouldPersist");
          if (mi != null)
            return (bool)mi.Invoke(component, null);

          mi = FindPropertyMethod(component, "ShouldSerialize");
          if (mi != null && !((bool)mi.Invoke(component, null)))
            return false;

          mi = FindPropertyMethod(component, "Reset");
          return mi != null;
        }
      }
#pragma warning restore 0618

#pragma warning disable 0618
      public override bool ShouldSerializeValue(object component)
      {
        component = MemberDescriptor.GetInvokee(_componentType, component);

        if (IsReadOnly)
        {
          MethodInfo mi = FindPropertyMethod(component, "ShouldSerialize");
          if (mi != null)
            return (bool)mi.Invoke(component, null);
          return Attributes.Contains(DesignerSerializationVisibilityAttribute.Content);
        }

        DefaultValueAttribute attrib = ((DefaultValueAttribute)Attributes[typeof(DefaultValueAttribute)]);
        if (attrib != null)
        {
          object current = GetValue(component);
          if (attrib.Value == null || current == null)
            return attrib.Value != current;
          return !attrib.Value.Equals(current);
        }
        else
        {
          MethodInfo mi = FindPropertyMethod(component, "ShouldSerialize");
          if (mi != null)
            return (bool)mi.Invoke(component, null);
          // MSDN: If this method cannot find a DefaultValueAttribute or a ShouldSerializeMyProperty method, 
          // it cannot create optimizations and it returns true. 
          return true;
        }
      }
    }
#pragma warning restore 0618

    #endregion
  }
}

