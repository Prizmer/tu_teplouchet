﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Ports;

namespace teplouchetapp
{
    public class teplouchet2_old : CMeter
    {
        public void Init(uint address, string pass, VirtualPort vp)
        {
            this.m_address = address;
            this.m_addr = (byte)(this.m_address & 0x000000ff);
            this.m_vport = vp;
        }
        
        public teplouchet2_old()
        {
            //опишем параметры запроса req_ud2
            List<RecordDescription> rdList = new List<RecordDescription>();

        }

        private byte m_addr;

        //private SerialPort m_port;

        const int REQ_UD2_HEADER_SIZE = 24;
        const int REQ_UD2_DATA_SIZE = 46;
        const int REQ_UD2_ANSW_SIZE = 72;

        struct RecordDescription
        {
            public RecordDescription(int index, byte size, byte cmd_size, float coeff)
            {
                this.index = index;
                this.size = size;
                this.cmd_size = cmd_size;
                this.coeff = coeff;
            }

            public int index;
            public byte size;
            public byte cmd_size;
            public float coeff;
        }

        RecordDescription energy = new RecordDescription(0, 6, 2, 1); //k = 1000 (Wh), 1 (kWh)
        RecordDescription volume = new RecordDescription(6, 6, 2, 1);
        RecordDescription time_on = new RecordDescription(12, 6, 2, 1);
        RecordDescription power = new RecordDescription(18, 6, 2, 1000);
        RecordDescription volflow = new RecordDescription(24, 6, 2, 0.001f);
        RecordDescription temp_in = new RecordDescription(30, 4, 2, 0.01f);
        RecordDescription temp_out = new RecordDescription(34, 4, 2, 0.01f);
        RecordDescription temp_diff = new RecordDescription(38, 4, 2, 0.01f);
       // RecordDescription dt = new RecordDescription(40, 4, 2, 1);

        bool SendREQ_UD2(ref byte[] data_arr)
        {
            byte cmd = 0x5B;
            byte CS = (byte)(cmd + m_addr);
            
            byte[] cmdArr = { 0x10, cmd, m_addr, CS, 0x16 };
            int firstRecordByteIndex = cmdArr.Length + 4 + 3 + 12;
            int lastRecordByteIndex = -1;

            byte[] inp = new byte[512];
            try
            {
                int readBytes = m_vport.WriteReadData(findPackageSign, cmdArr, ref inp, cmdArr.Length, -1);
                for (int i = inp.Length - 1; i >= 0; i--)
                    if (inp[i] == 0x16)
                    {
                        lastRecordByteIndex = i - 2;
                        break;
                    }

                List<byte> UserDataBlock = new List<byte>();
                for (int i = firstRecordByteIndex; i <= lastRecordByteIndex; i++)
                    UserDataBlock.Add(inp[i]);

                if (UserDataBlock.Count == 0) return false;
                data_arr = UserDataBlock.ToArray();
                    return true;

            }
            catch (Exception ex)
            {
                WriteToLog("SendREQ_UD2: " + ex.Message);
                return false;
            }
        }

        bool SND_NKE(ref bool confirmed)
        {
           byte cmd = 0x40;
            byte CS = (byte)(cmd + m_addr);
            
            byte[] cmdArr = { 0x10, cmd, m_addr, CS, 0x16 };
            int firstRecordByteIndex = cmdArr.Length + 4 + 3 + 12;

            byte[] inp = new byte[512];
            try
            {
                int readBytes = m_vport.WriteReadData(findPackageSign, cmdArr, ref inp, cmdArr.Length, -1);
                if (inp[readBytes-1] == 0xE5)
                    confirmed = true;
                else
                    confirmed = false;

                return true;
            }
            catch (Exception ex)
            {
                WriteToLog("SendREQ_UD2: " + ex.Message);
                return false;
            }

        }

        public bool ToBcd(int value, ref byte[] byteArr)
        {
            if (value < 0 || value > 99999999)
                return false;

            byte[] ret = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                ret[i] = (byte)(value % 10);
                value /= 10;
                ret[i] |= (byte)((value % 10) << 4);
                value /= 10;
            }

            Array.Reverse(ret);
            byteArr = ret;

            return true;
        }

        public bool SelectBySecondaryId(int factoryNumber)
        {
            byte cmd = 0x53;
            byte CI = 0x52;

            byte[] addrArr = null;
            if (!ToBcd(factoryNumber, ref addrArr))
                return false;

            byte CS = (byte)(cmd + m_addr + CI + addrArr[3] + addrArr[2] + addrArr[1] + addrArr[0] + 0xFF + 0xFF + 0xFF + 0xFF);

            byte[] cmdArr = { 0x68, 0x0B, 0x0B, 0x68, cmd, m_addr, CI, addrArr[3], addrArr[2], addrArr[1], addrArr[0], 
                                0xFF, 0xFF , 0xFF,0xFF, CS, 0x16 };
            int firstRecordByteIndex = cmdArr.Length + 4 + 3 + 12;


            byte[] inp = new byte[512];
            try
            {
                int readBytes = m_vport.WriteReadData(findPackageSign, cmdArr, ref inp, cmdArr.Length, -1);
                for (int i = inp.Length - 1; i >= 0; i--)
                    if (inp[i] == 0xE5)
                    {
                        return true;
                    }

                WriteToLog("SelectBySecondaryId: в ответе не найден байт подтверждения 0xE5");
                return false;

            }
            catch (Exception ex)
            {
                WriteToLog("SelectBySecondaryId: " + ex.Message);
                return false;
            }
        }
        public bool UnselectAllMeters()
        {
            bool res = false;
            this.SND_NKE(ref res);

            return res;
        }



        int findPackageSign(Queue<byte> queue)
        {
            return 0;
        }

        public bool OpenLinkCanal()
        {
            bool confirmation = false;
            if (!SND_NKE(ref confirmation))
                return false;

            if (!confirmation) return false;

            return true;
        }

        /// <summary>
        /// Чтение текущих значений
        /// </summary>
        /// <param name="values">Возвращаемые данные</param>
        /// <returns></returns>
        public bool ReadCurrentValues(ushort param, ushort tarif, ref float recordValue)
        {
            switch (param)
            {
                case 1: return parseParam(energy, ref recordValue);
                case 2: return parseParam(volume, ref recordValue);
                case 3: return parseParam(power, ref recordValue); 
                case 4: return parseParam(volflow, ref recordValue); //volume flow (m3*h)
                case 5: return parseParam(time_on, ref recordValue);
                case 6: return parseParam(temp_in, ref recordValue);
                case 7: return parseParam(temp_out, ref recordValue);
                default:
                    {
                        WriteToLog("ReadCurrentValues: для параметра " + param.ToString() + " нет обработчика");
                        return false;
                    }
            }
        }

        /// <summary>
        /// Чтение текущих значений (всех)
        /// </summary>
        /// <param name="valDict"></param>
        /// <returns></returns>
        public bool ReadCurrentValues(ref Dictionary<string, float> valDict)
        {
            valDict = new Dictionary<string, float>(6);

            byte[] data = null;
            if (SendREQ_UD2(ref data))
            {
                float[] tmpVal = new float[6];
                if (!parseParam(data, energy, ref tmpVal[0])) tmpVal[0] = -1;
                if (!parseParam(data, volume, ref tmpVal[1])) tmpVal[1] = -1;
                if (!parseParam(data, power, ref tmpVal[2])) tmpVal[2] = -1;
                if (!parseParam(data, temp_in, ref tmpVal[3])) tmpVal[3] = -1;
                if (!parseParam(data, temp_out, ref tmpVal[4])) tmpVal[4] = -1;
                if (!parseParam(data, time_on, ref tmpVal[5])) tmpVal[5] = -1;

                valDict.Add("energy", tmpVal[0]);
                valDict.Add("volume", tmpVal[1]);
                valDict.Add("power", tmpVal[2]);
                valDict.Add("temp_in", tmpVal[3]);
                valDict.Add("temp_out", tmpVal[4]);
                valDict.Add("time_on", tmpVal[5]);

                return true;
            }
            else
            {
                return false;
            }
        }

        #region Расчет контрольной суммы
        // CRC-8 for Dallas iButton products from Maxim/Dallas AP Note 27
        readonly byte[] crc8Table = new byte[]
        {
            0x00, 0x5E, 0xBC, 0xE2, 0x61, 0x3F, 0xDD, 0x83,
            0xC2, 0x9C, 0x7E, 0x20, 0xA3, 0xFD, 0x1F, 0x41,
            0x9D, 0xC3, 0x21, 0x7F, 0xFC, 0xA2, 0x40, 0x1E,
            0x5F, 0x01, 0xE3, 0xBD, 0x3E, 0x60, 0x82, 0xDC,
            0x23, 0x7D, 0x9F, 0xC1, 0x42, 0x1C, 0xFE, 0xA0,
            0xE1, 0xBF, 0x5D, 0x03, 0x80, 0xDE, 0x3C, 0x62,
            0xBE, 0xE0, 0x02, 0x5C, 0xDF, 0x81, 0x63, 0x3D,
            0x7C, 0x22, 0xC0, 0x9E, 0x1D, 0x43, 0xA1, 0xFF,
            0x46, 0x18, 0xFA, 0xA4, 0x27, 0x79, 0x9B, 0xC5,
            0x84, 0xDA, 0x38, 0x66, 0xE5, 0xBB, 0x59, 0x07,
            0xDB, 0x85, 0x67, 0x39, 0xBA, 0xE4, 0x06, 0x58,
            0x19, 0x47, 0xA5, 0xFB, 0x78, 0x26, 0xC4, 0x9A,
            0x65, 0x3B, 0xD9, 0x87, 0x04, 0x5A, 0xB8, 0xE6,
            0xA7, 0xF9, 0x1B, 0x45, 0xC6, 0x98, 0x7A, 0x24,
            0xF8, 0xA6, 0x44, 0x1A, 0x99, 0xC7, 0x25, 0x7B,
            0x3A, 0x64, 0x86, 0xD8, 0x5B, 0x05, 0xE7, 0xB9,
            0x8C, 0xD2, 0x30, 0x6E, 0xED, 0xB3, 0x51, 0x0F,
            0x4E, 0x10, 0xF2, 0xAC, 0x2F, 0x71, 0x93, 0xCD,
            0x11, 0x4F, 0xAD, 0xF3, 0x70, 0x2E, 0xCC, 0x92,
            0xD3, 0x8D, 0x6F, 0x31, 0xB2, 0xEC, 0x0E, 0x50,
            0xAF, 0xF1, 0x13, 0x4D, 0xCE, 0x90, 0x72, 0x2C,
            0x6D, 0x33, 0xD1, 0x8F, 0x0C, 0x52, 0xB0, 0xEE,
            0x32, 0x6C, 0x8E, 0xD0, 0x53, 0x0D, 0xEF, 0xB1,
            0xF0, 0xAE, 0x4C, 0x12, 0x91, 0xCF, 0x2D, 0x73,
            0xCA, 0x94, 0x76, 0x28, 0xAB, 0xF5, 0x17, 0x49,
            0x08, 0x56, 0xB4, 0xEA, 0x69, 0x37, 0xD5, 0x8B,
            0x57, 0x09, 0xEB, 0xB5, 0x36, 0x68, 0x8A, 0xD4,
            0x95, 0xCB, 0x29, 0x77, 0xF4, 0xAA, 0x48, 0x16,
            0xE9, 0xB7, 0x55, 0x0B, 0x88, 0xD6, 0x34, 0x6A,
            0x2B, 0x75, 0x97, 0xC9, 0x4A, 0x14, 0xF6, 0xA8,
            0x74, 0x2A, 0xC8, 0x96, 0x15, 0x4B, 0xA9, 0xF7,
            0xB6, 0xE8, 0x0A, 0x54, 0xD7, 0x89, 0x6B, 0x35
        };

        public byte CRC8(byte[] bytes, int len)
        {
            byte crc = 0;
            for (var i = 0; i < len; i++)
                crc = crc8Table[crc ^ bytes[i]];

            //byte[] crcArr = new byte[1];
            // crcArr[0] = crc;
            //MessageBox.Show(BitConverter.ToString(crcArr));
            return crc;
        }

        #endregion

        bool parseParam(RecordDescription rd, ref float value)
        {
            byte[] data = null;
            if (SendREQ_UD2(ref data))
            {
                /*энергия записана в 6ти кодебайтах в hex-dec*/
                byte[] energyBytes = new byte[rd.size];
                Array.Copy(data, rd.index, energyBytes, 0, rd.size);
                Array.Reverse(energyBytes, rd.cmd_size, energyBytes.Length - rd.cmd_size);

                string hex_str = BitConverter.ToString(energyBytes, rd.cmd_size).Replace("-", string.Empty);

                float temp_val = (float)Convert.ToDouble(hex_str) * rd.coeff;

                value = temp_val;
                return true;
            }
            else
            {
                return false;
            }
        }

        bool parseParam(byte[] data, RecordDescription rd, ref float value)
        {
            List<byte> dataBytesList = new List<byte>();
            if (rd.index + rd.size >= data.Length)
            {
                WriteToLog("rd.index + rd.size >= data.Length");
                return false;
            }


            for (int i = rd.index + rd.cmd_size; i < rd.index + rd.size; i++)
            {
                dataBytesList.Add(data[i]);
            }

            int dif = 4 - dataBytesList.Count;
           if (dif > 0 ) {
               for (int i = 0; i < dif; i++){
                   dataBytesList.Add(0x0);
               }
           }

           dataBytesList.Reverse();

           string hex_str = "";
           float temp_val = -1f;
           try
           {
               hex_str = BitConverter.ToString(dataBytesList.ToArray(), 0).Replace("-", string.Empty);
               temp_val = (float)Convert.ToSingle(hex_str) * rd.coeff;
           }
           catch (Exception ex)
           {
               WriteToLog(ex.Message + " conversion problems");
               return false;
           }

           // string hstr = BitConverter.ToString(energyBytes, rd.cmd_size);
           value = temp_val;
           if (temp_val > 30000)
           {
               value = 0f;
           }
            return true;
        }


        public bool ReadMonthlyValues(DateTime dt, ushort param, ushort tarif, ref float recordValue)
        {
            if (dt.Date.Day == 1)
            {
                try
                {
                    ReadCurrentValues(param, tarif, ref recordValue);
                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// Преобразует дату в идентификатор архивной записи и возвращает значение в соответствии с 
        /// указанным param. Правильное преобразование не гарантируется. 
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="param"></param>
        /// <param name="tarif"></param>
        /// <param name="recordValue"></param>
        /// <returns></returns>
        public bool ReadDailyValues(DateTime dt, ushort param, ushort tarif, ref float recordValue)
        {    
            if (dt.TimeOfDay.Hours == 0)
            {
                try
                {
                    ReadCurrentValues(param, tarif, ref recordValue);
                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
            return false;
        }


        public bool SyncTime(DateTime dt)
        {
            return false;
        }

        public bool ReadSerialNumber(ref string serial_number)
        {
            return false;
        }


        public bool ReadDailyValues(uint recordId, ushort param, ushort tarif, ref float recordValue)
        {
            return false;
        }



        public bool ReadSliceArrInitializationDate(ref DateTime lastInitDt)
        {
            return false;
        }


        public bool ReadHalfAnHourValues(DateTime dt, ushort param, ushort tarif, ref float recordValue)
        {
            throw new NotImplementedException();
        }

        public bool ReadHourValues(DateTime dt, ushort param, ushort tarif, ref float recordValue)
        {
            throw new NotImplementedException();
        }
    }




}
