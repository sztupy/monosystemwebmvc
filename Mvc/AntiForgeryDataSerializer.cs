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
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Web;
    using System.Web.Mvc.Resources;
    using System.Web.UI;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Security.Cryptography;
using System.Text;

    internal class AntiForgeryDataSerializer {

        private IStateFormatter _formatter;

        protected internal IStateFormatter Formatter {
            get {
                if (_formatter == null) {
                    _formatter = FormatterGenerator.GetFormatter();
                }
                return _formatter;
            }
            set {
                _formatter = value;
            }
        }

        private static HttpAntiForgeryException CreateValidationException(Exception innerException) {
            return new HttpAntiForgeryException(MvcResources.AntiForgeryToken_ValidationFailed, innerException);
        }

        public virtual AntiForgeryData Deserialize(string serializedToken) {
            if (String.IsNullOrEmpty(serializedToken)) {
                throw new ArgumentException(MvcResources.Common_NullOrEmpty, "serializedToken");
            }

            // call property getter outside try { } block so that exceptions bubble up for debugging
            IStateFormatter formatter = Formatter;

            try {
                object[] deserializedObj = (object[])formatter.Deserialize(serializedToken);
                return new AntiForgeryData() {
                    Salt = (string)deserializedObj[0],
                    Value = (string)deserializedObj[1],
                    CreationDate = (DateTime)deserializedObj[2],
                    Username = (string)deserializedObj[3]
                };
            }
            catch (Exception ex) {
                throw CreateValidationException(ex);
            }
        }

        public virtual string Serialize(AntiForgeryData token) {
            if (token == null) {
                throw new ArgumentNullException("token");
            }

            object[] objToSerialize = new object[] {
                token.Salt,
                token.Value,
                token.CreationDate,
                token.Username
            };

            string serializedValue = Formatter.Serialize(objToSerialize);
            return serializedValue;
        }

        // See http://www.yoda.arachsys.com/csharp/singleton.html (fifth version - fully lazy) for the singleton pattern
        // used here. We need to defer the call to TokenPersister.CreateFormatterGenerator() until we're actually
        // servicing a request, else HttpContext.Current might be invalid in TokenPersister.CreateFormatterGenerator().
        private static class FormatterGenerator {

            public static readonly Func<IStateFormatter> GetFormatter = TokenPersister.CreateFormatterGenerator();

            [SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline",
                Justification = "This type must not be marked 'beforefieldinit'.")]
            static FormatterGenerator() {
            }

            // This type is very difficult to unit-test because Page.ProcessRequest() requires mocking
            // much of the hosting environment. For now, we can perform functional tests of this feature.
            private sealed class TokenPersister : PageStatePersister {
                private TokenPersister(Page page)
                    : base(page) {
                }

                public static Func<IStateFormatter> CreateFormatterGenerator() {
                    // This code instantiates a page and tricks it into thinking that it's servicing
                    // a postback scenario with encrypted ViewState, which is required to make the
                    // StateFormatter properly decrypt data. Specifically, this code sets the
                    // internal Page.ContainsEncryptedViewState flag.

                    // MONO: If creating the page fails, fall back to the generated cookie
                    try
                    {
                        TextWriter writer = TextWriter.Null;
                        HttpResponse response = new HttpResponse(writer);
                        HttpRequest request = new HttpRequest("DummyFile.aspx", HttpContext.Current.Request.Url.ToString(), "__EVENTTARGET=true&__VIEWSTATEENCRYPTED=true");
                        HttpContext context = new HttpContext(request, response);

                        Page page = new Page()
                        {
                            EnableViewStateMac = true,
                            ViewStateEncryptionMode = ViewStateEncryptionMode.Always
                        };
                        page.ProcessRequest(context);

                        return () => new TokenPersister(page).StateFormatter;
                    }
                    catch (Exception)
                    {
                        return () => new SimplePersister();
                    }
                }

                public override void Load() {
                    throw new NotImplementedException();
                }

                public override void Save() {
                    throw new NotImplementedException();
                }

                // If the TokenPersister throws an error message we serialize the object using binary serialization.
                // I couldn't find a way to retrieve the machineKey/decryptionKey associated with the page, so
                // it is hardcoded here, which is considered a security risk.
                private sealed class SimplePersister : IStateFormatter
                {
                    const string key = "S.S_f%vd8nVSe~6m4?$-XcgK+CJ7}FpX,]o+:r>'_;na)xJ|],<JL,g%*b^Pe,C";
                    const string salt = "6D64DEED7D5E4DE42B530C950A76D9C890C6FC432B3AB377648A90F3C653BAF";
                    const string iv = "DmaMztYcw0Km1SaJ";


                    public object Deserialize(string serializedState)
                    {
                        string decrypted = AESEncryption.Decrypt(serializedState, key, salt, "SHA1", 2, iv, 256);
                        byte[] decoded = System.Convert.FromBase64String(decrypted);
                        MemoryStream memoryStream = new MemoryStream(decoded);
                        BinaryFormatter binaryFormatter = new BinaryFormatter();
                        return binaryFormatter.Deserialize(memoryStream);
                    }

                    public string Serialize(object state)
                    {
                        MemoryStream memoryStream = new MemoryStream();
                        BinaryFormatter binaryFormatter = new BinaryFormatter();
                        binaryFormatter.Serialize(memoryStream, state);
                        string serialized = System.Convert.ToBase64String(memoryStream.ToArray());
                        string encrypted = AESEncryption.Encrypt(serialized, key, salt, "SHA1", 2, iv, 256);
                        return encrypted;
                    }
                }

                /// <summary>
                /// Utility class that handles encryption
                /// </summary>
                private static class AESEncryption
                {
                    #region Static Functions
                    /// <summary>
                    /// Encrypts a string
                    /// </summary>
                    /// <param name="PlainText">Text to be encrypted</param>
                    /// <param name="Password">Password to encrypt with</param>
                    /// <param name="Salt">Salt to encrypt with</param>
                    /// <param name="HashAlgorithm">Can be either SHA1 or MD5</param>
                    /// <param name="PasswordIterations">Number of iterations to do</param>
                    /// <param name="InitialVector">Needs to be 16 ASCII characters long</param>
                    /// <param name="KeySize">Can be 128, 192, or 256</param>
                    /// <returns>An encrypted string</returns>
                    public static string Encrypt(string PlainText, string Password, string Salt, string HashAlgorithm, int PasswordIterations, string InitialVector, int KeySize)
                    {
                        try
                        {
                            byte[] InitialVectorBytes = Encoding.ASCII.GetBytes(InitialVector);
                            byte[] SaltValueBytes = Encoding.ASCII.GetBytes(Salt);
                            byte[] PlainTextBytes = Encoding.UTF8.GetBytes(PlainText);
                            PasswordDeriveBytes DerivedPassword = new PasswordDeriveBytes(Password, SaltValueBytes, HashAlgorithm, PasswordIterations);
                            byte[] KeyBytes = DerivedPassword.GetBytes(KeySize / 8);
                            RijndaelManaged SymmetricKey = new RijndaelManaged();
                            SymmetricKey.Mode = CipherMode.CBC;
                            byte[] CipherTextBytes = null;
                            using (ICryptoTransform Encryptor = SymmetricKey.CreateEncryptor(KeyBytes, InitialVectorBytes))
                            {
                                using (MemoryStream MemStream = new MemoryStream())
                                {
                                    using (CryptoStream CryptoStream = new CryptoStream(MemStream, Encryptor, CryptoStreamMode.Write))
                                    {
                                        CryptoStream.Write(PlainTextBytes, 0, PlainTextBytes.Length);
                                        CryptoStream.FlushFinalBlock();
                                        CipherTextBytes = MemStream.ToArray();
                                        MemStream.Close();
                                        CryptoStream.Close();
                                    }
                                }
                            }
                            return Convert.ToBase64String(CipherTextBytes);
                        }
                        catch (Exception a)
                        {
                            throw a;
                        }
                    }

                    /// <summary>
                    /// Decrypts a string
                    /// </summary>
                    /// <param name="CipherText">Text to be decrypted</param>
                    /// <param name="Password">Password to decrypt with</param>
                    /// <param name="Salt">Salt to decrypt with</param>
                    /// <param name="HashAlgorithm">Can be either SHA1 or MD5</param>
                    /// <param name="PasswordIterations">Number of iterations to do</param>
                    /// <param name="InitialVector">Needs to be 16 ASCII characters long</param>
                    /// <param name="KeySize">Can be 128, 192, or 256</param>
                    /// <returns>A decrypted string</returns>
                    public static string Decrypt(string CipherText, string Password, string Salt, string HashAlgorithm, int PasswordIterations, string InitialVector, int KeySize)
                    {
                        try
                        {
                            byte[] InitialVectorBytes = Encoding.ASCII.GetBytes(InitialVector);
                            byte[] SaltValueBytes = Encoding.ASCII.GetBytes(Salt);
                            byte[] CipherTextBytes = Convert.FromBase64String(CipherText);
                            PasswordDeriveBytes DerivedPassword = new PasswordDeriveBytes(Password, SaltValueBytes, HashAlgorithm, PasswordIterations);
                            byte[] KeyBytes = DerivedPassword.GetBytes(KeySize / 8);
                            RijndaelManaged SymmetricKey = new RijndaelManaged();
                            SymmetricKey.Mode = CipherMode.CBC;
                            byte[] PlainTextBytes = new byte[CipherTextBytes.Length];
                            int ByteCount = 0;
                            using (ICryptoTransform Decryptor = SymmetricKey.CreateDecryptor(KeyBytes, InitialVectorBytes))
                            {
                                using (MemoryStream MemStream = new MemoryStream(CipherTextBytes))
                                {
                                    using (CryptoStream CryptoStream = new CryptoStream(MemStream, Decryptor, CryptoStreamMode.Read))
                                    {

                                        ByteCount = CryptoStream.Read(PlainTextBytes, 0, PlainTextBytes.Length);
                                        MemStream.Close();
                                        CryptoStream.Close();
                                    }
                                }
                            }
                            return Encoding.UTF8.GetString(PlainTextBytes, 0, ByteCount);
                        }
                        catch (Exception a)
                        {
                            throw a;
                        }
                    }
                    #endregion
                }
            }
        }

    }
}
