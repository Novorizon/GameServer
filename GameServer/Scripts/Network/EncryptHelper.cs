
using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace Net
{
    public static class EncryptHelper
    {

        private static string publicKey = @"-----BEGIN PUBLIC KEY-----
MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQCe8zGb4UAMg2A63pH+/W145hHv
YQPJlkX6OfzJ1215htCI6Pyh2TdHRrDqVU6wP609ao9tLxRsbbXrajBGXiq2ijRX
7AKrsVdhYi2J+B2q/CrsH5CDKa16YCVPPwf/oZDz/hxrcjZjhOoSIZupY3/xzOBT
TjcVcvWbTxGw0wOm6wIDAQAB
-----END PUBLIC KEY-----";
        private static string privateKey = "";
        public static void RSAKey(out string xmlKeys, out string xmlPublicKey)
        {
            try
            {
                System.Security.Cryptography.RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();
                xmlKeys = rsa.ToXmlString(true);
                xmlPublicKey = rsa.ToXmlString(false);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        public static RSAParameters ConvertFromPemPublicKey(string pemFileConent)
        {
            if (string.IsNullOrEmpty(pemFileConent))
            {
                throw new ArgumentNullException("pemFileConent", "This arg cann't be empty.");
            }
            pemFileConent = pemFileConent.Replace("-----BEGIN PUBLIC KEY-----", "").Replace("-----END PUBLIC KEY-----", "").Replace("\n", "").Replace("\r", "");
            byte[] keyData = Encoding.UTF8.GetBytes(pemFileConent);
            if (keyData.Length < 162)
            {
                throw new ArgumentException("pem file content is incorrect.");
            }
            byte[] pemModulus = new byte[128];
            byte[] pemPublicExponent = new byte[3];
            Array.Copy(keyData, 29, pemModulus, 0, 128);
            Array.Copy(keyData, 159, pemPublicExponent, 0, 3);
            RSAParameters para = new RSAParameters();
            para.Modulus = pemModulus;
            para.Exponent = pemPublicExponent;
            return para;
        }
        public static RSAParameters ConvertFromPemPrivateKey(string pemFileConent)
        {
            if (string.IsNullOrEmpty(pemFileConent))
            {
                throw new ArgumentNullException("pemFileConent", "This arg cann't be empty.");
            }
            pemFileConent = pemFileConent.Replace("-----BEGIN RSA PRIVATE KEY-----", "").Replace("-----END RSA PRIVATE KEY-----", "").Replace("\n", "").Replace("\r", "");
            byte[] keyData = Convert.FromBase64String(pemFileConent);
            if (keyData.Length < 609)
            {
                throw new ArgumentException("pem file content is incorrect.");
            }

            int index = 11;
            byte[] pemModulus = new byte[128];
            Array.Copy(keyData, index, pemModulus, 0, 128);

            index += 128;
            index += 2;//141
            byte[] pemPublicExponent = new byte[3];
            Array.Copy(keyData, index, pemPublicExponent, 0, 3);

            index += 3;
            index += 4;//148
            byte[] pemPrivateExponent = new byte[128];
            Array.Copy(keyData, index, pemPrivateExponent, 0, 128);

            index += 128;
            index += ((int)keyData[index + 1] == 64 ? 2 : 3);//279
            byte[] pemPrime1 = new byte[64];
            Array.Copy(keyData, index, pemPrime1, 0, 64);

            index += 64;
            index += ((int)keyData[index + 1] == 64 ? 2 : 3);//346
            byte[] pemPrime2 = new byte[64];
            Array.Copy(keyData, index, pemPrime2, 0, 64);

            index += 64;
            index += ((int)keyData[index + 1] == 64 ? 2 : 3);//412/413
            byte[] pemExponent1 = new byte[64];
            Array.Copy(keyData, index, pemExponent1, 0, 64);

            index += 64;
            index += ((int)keyData[index + 1] == 64 ? 2 : 3);//479/480
            byte[] pemExponent2 = new byte[64];
            Array.Copy(keyData, index, pemExponent2, 0, 64);

            index += 64;
            index += ((int)keyData[index + 1] == 64 ? 2 : 3);//545/546
            byte[] pemCoefficient = new byte[64];
            Array.Copy(keyData, index, pemCoefficient, 0, 64);

            RSAParameters para = new RSAParameters();
            para.Modulus = pemModulus;
            para.Exponent = pemPublicExponent;
            para.D = pemPrivateExponent;
            para.P = pemPrime1;
            para.Q = pemPrime2;
            para.DP = pemExponent1;
            para.DQ = pemExponent2;
            para.InverseQ = pemCoefficient;
            return para;
        }
        public static byte[] RSAEncrypt(byte[] data)
        {
            //创建RSA对象并载入[公钥]
            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();
            //RSAParameters para = ConvertFromPemPublicKey(publicKey);
            //rsa.ImportParameters(para);
            //string key1;
            //string key2;
            //RSAKey(out key1, out key2);
            //string str = rsa.ToString();
            string xmlPublicKey = "<RSAKeyValue><Modulus>nvMxm+FADINgOt6R/v1teOYR72EDyZZF+jn8yddteYbQiOj8odk3R0aw6lVOsD+tPWqPbS8UbG2162owRl4qtoo0V+wCq7FXYWItifgdqvwq7B+QgymtemAlTz8H/6GQ8/4ca3I2Y4TqEiGbqWN/8czgU043FXL1m08RsNMDpus=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
            rsa.FromXmlString(xmlPublicKey);
            //对数据进行加密
            byte[] inputByte = data;
            byte[] outData = rsa.Encrypt(data, true);
            string temp = ToHexString(outData);
            //string publicStr = Convert.ToBase64String(publicValue);//使用Base64将byte转换为string
            return outData;
        }
        public static string RSADecrypt(string data)
        {
            RSACryptoServiceProvider rsaPrivate = new RSACryptoServiceProvider();
            rsaPrivate.FromXmlString(privateKey);
            byte[] privateValue = rsaPrivate.Decrypt(Convert.FromBase64String(data), false);//使用Base64将string转换为byte
            string privateStr = Encoding.UTF8.GetString(privateValue);
            return privateStr;
        }

        public static string AESEncrypt(string plainText, string AESKey)
        {
            RijndaelManaged rijndaelCipher = new RijndaelManaged();
            byte[] inputByteArray = Encoding.UTF8.GetBytes(plainText);//得到需要加密的字节数组
            rijndaelCipher.Key = Encoding.UTF8.GetBytes(AESKey);//加解密双方约定好密钥：AESKey
            rijndaelCipher.GenerateIV();
            byte[] keyIv = rijndaelCipher.IV;
            byte[] cipherBytes = null;
            using (MemoryStream ms = new MemoryStream())
            {
                using (CryptoStream cs = new CryptoStream(ms, rijndaelCipher.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    cs.Write(inputByteArray, 0, inputByteArray.Length);
                    cs.FlushFinalBlock();
                    cipherBytes = ms.ToArray();//得到加密后的字节数组
                    cs.Close();
                    ms.Close();
                }
            }
            var allEncrypt = new byte[keyIv.Length + cipherBytes.Length];
            Buffer.BlockCopy(keyIv, 0, allEncrypt, 0, keyIv.Length);
            Buffer.BlockCopy(cipherBytes, 0, allEncrypt, keyIv.Length * sizeof(byte), cipherBytes.Length);
            string temp = ToHexString(allEncrypt);
            return Convert.ToBase64String(allEncrypt);
        }
        public static string ToHexString(byte[] bytes) // 0xae00cf => "AE00CF "

        {
            string hexString = string.Empty;

            if (bytes != null)

            {

                StringBuilder strB = new StringBuilder();

                for (int i = 0; i < bytes.Length; i++)

                {

                    strB.Append(bytes[i].ToString("X2"));

                }

                hexString = strB.ToString();

            }
            return hexString;

        }
        public static string Encrypt(string key, string toEncrypt)
        {
            byte[] keyArray = UTF8Encoding.UTF8.GetBytes(key);
            byte[] toEncryptArray = UTF8Encoding.UTF8.GetBytes(toEncrypt);

            RijndaelManaged rDel = new RijndaelManaged();
            rDel.Key = keyArray;
            rDel.Mode = CipherMode.CFB;
            rDel.IV = keyArray;
            //rDel.FeedbackSize = 8;

            ICryptoTransform cTransform = rDel.CreateEncryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);

            return Convert.ToBase64String(resultArray, 0, resultArray.Length);
        }
        public static byte[] AESEncrypt(byte[] Data, byte[] Key)
        {
            MemoryStream mStream = new MemoryStream();
            RijndaelManaged aes = new RijndaelManaged();

            byte[] plainBytes = Data;
            //Byte[] IV = new Byte[16];
            //Key.CopyTo(IV, 0);

            aes.Mode = CipherMode.CFB;
            //aes.Padding = PaddingMode.None;
            //aes.BlockSize = 16;
            //aes.KeySize = 16;
            aes.Key = Key;
            aes.IV = Key;
            /*
            CryptoStream cryptoStream = new CryptoStream(mStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
            try
            {
                cryptoStream.Write(plainBytes, 0, plainBytes.Length);
                //cryptoStream.FlushFinalBlock();
                return mStream.ToArray();
            }
            finally
            {
                cryptoStream.Close();
                mStream.Close();
                aes.Clear();
            }
            */
            ICryptoTransform cTransform = aes.CreateEncryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(Data, 0, Data.Length);
            return resultArray;
        }

        public static byte[] AESDecrypt(Byte[] Data, byte[] Key)
        {
            //Byte[] encryptedBytes = Data;
            //Byte[] iv = new Byte[16];
            //Key.CopyTo(iv, 0);           
            //MemoryStream mStream = new MemoryStream(encryptedBytes);
            RijndaelManaged aes = new RijndaelManaged();
            aes.Mode = CipherMode.CFB;
            aes.Padding = PaddingMode.None;
            //aes.KeySize = 16;
            aes.Key = Key;
            aes.IV = Key;

            ICryptoTransform cTransform = aes.CreateDecryptor();
            Byte[] resultArray = cTransform.TransformFinalBlock(Data, 0, Data.Length);

            return resultArray;

            /*
            CryptoStream cryptoStream = new CryptoStream(mStream, aes.CreateDecryptor(), CryptoStreamMode.Read);
            try
            {
                byte[] tmp = new byte[Key.Length];
                int len = cryptoStream.Read(tmp, 0, Key.Length);
                byte[] ret = new byte[len];
                Array.Copy(tmp, 0, ret, 0, len);
                return ret;
            }
            finally
            {
                cryptoStream.Close();
                mStream.Close();
                aes.Clear();
            }
            */
        }
        public static string AESDecrypt(string showText, byte[] AESKey)
        {
            string result = string.Empty;
            try
            {
                byte[] cipherText = Convert.FromBase64String(showText);
                int length = cipherText.Length;
                SymmetricAlgorithm rijndaelCipher = Rijndael.Create();
                rijndaelCipher.Key = AESKey;//加解密双方约定好的密钥
                rijndaelCipher.Mode = CipherMode.CFB;
                byte[] iv = new byte[16];
                Buffer.BlockCopy(cipherText, 0, iv, 0, 16);
                rijndaelCipher.IV = iv;
                byte[] decryptBytes = new byte[length - 16];
                byte[] passwdText = new byte[length - 16];
                Buffer.BlockCopy(cipherText, 16, passwdText, 0, length - 16);
                using (MemoryStream ms = new MemoryStream(passwdText))
                {
                    using (CryptoStream cs = new CryptoStream(ms, rijndaelCipher.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        cs.Read(decryptBytes, 0, decryptBytes.Length);
                        cs.Close();
                        ms.Close();
                    }
                }
                result = Encoding.UTF8.GetString(decryptBytes).Replace("\0", "");   ///将字符串后尾的'\0'去掉
            }
            catch { }
            return result;
        }
    }


}
