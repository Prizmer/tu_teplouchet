using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Ports;

namespace teplouchetapp
{
    public class teplouchet1 : CMeter
    {
        public void Init(uint address, string pass, VirtualPort vp)
        {
            this.m_address = address;
            this.m_addr = (byte)(this.m_address & 0x000000ff);
            this.m_vport = vp;
        }
        
        public teplouchet1()
        {

        }

        private byte m_addr;


        /*
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
        */

        /*
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
        */

        #region Протокол MBUS

        public struct Record
        {
            public byte DIF;
            public List<byte> DIFEs;

            public byte VIF;
            public List<byte> VIFEs;

            public List<byte> dataBytes;

            public RecordDataType recordType;
        }

        public enum RecordDataType
        {
            NO_DATA = 0,
            INTEGER = 1,
            REAL = 2,
            BCD = 3,
            VARIABLE_LENGTH = 4,
            SELECTION_FOR_READOUT = 5,
            SPECIAL_FUNСTIONS = 6
        }

        //параметры в порядке как они идут в rsp_ud
        public enum Params
        {
            ENERGY = 0,
            VOLUME = 1,
            TIME_ON = 2,
            POWER = 3,
            VOLUME_FLOW = 4,
            TEMP_INP = 5,
            TEMP_OUTP = 6,
            TEMP_DIFF = 7,
            DATE = 8
        }

        public int getLengthAndTypeFromDIF(byte DIF, out RecordDataType type)
        {
            int data = DIF & 0x0F; //00001111b
            switch (data)
            {
                case 0:
                    {
                        type = RecordDataType.NO_DATA;
                        return 0;
                    }
                case 1:
                    {
                        type = RecordDataType.INTEGER;
                        return 1;
                    }
                case 2:
                    {
                        type = RecordDataType.INTEGER;
                        return 2;
                    }
                case 3:
                    {
                        type = RecordDataType.INTEGER;
                        return 3;
                    }
                case 4:
                    {
                        type = RecordDataType.INTEGER;
                        return 4;
                    }
                case 5:
                    {
                        WriteToLog("getLengthAndTypeFromDIF: 5, real");
                        type = RecordDataType.REAL;
                        return 4;
                    }
                case 6:
                    {
                        type = RecordDataType.INTEGER;
                        return 6;
                    }
                case 7:
                    {
                        type = RecordDataType.INTEGER;
                        return 8;
                    }
                case 8:
                    {
                        //selection for readout
                        WriteToLog("getLengthAndTypeFromDIF: 8, selection for readout");
                        type = RecordDataType.SELECTION_FOR_READOUT;
                        return 0;
                    }
                case 9:
                    {
                        type = RecordDataType.BCD;
                        return 1;
                    }
                case 10:
                    {
                        type = RecordDataType.BCD;
                        return 2;
                    }
                case 11:
                    {
                        type = RecordDataType.BCD;
                        return 3;
                    }
                case 12:
                    {
                        type = RecordDataType.BCD;
                        return 4;
                    }
                case 13:
                    {
                        WriteToLog("getLengthAndTypeFromDIF: 13, variable length");
                        type = RecordDataType.VARIABLE_LENGTH;
                        return -1;
                    }
                case 14:
                    {
                        type = RecordDataType.BCD;
                        return 6;
                    }
                case 15:
                    {
                        WriteToLog("getLengthAndTypeFromDIF: 15, special functions");
                        type = RecordDataType.SPECIAL_FUNСTIONS;
                        return -1;
                    }
                default:
                    {
                        type = RecordDataType.NO_DATA;
                        return -1;
                    }
            }
        }

        private bool getRecordValueByParam(Params param, List<Record> records, out float value)
        {
            if (records == null && records.Count == 0)
            {
                WriteToLog("getRecordValueByParam: список записей пуст");
                value = 0f;
                return false;
            }

            if ((int)param >= records.Count)
            {
                WriteToLog("getRecordValueByParam: параметра не существует в списке записей: " + param.ToString());
                value = 0f;
                return false;
            }

            Record record = records[(int)param];
            byte[] data = record.dataBytes.ToArray();
            Array.Reverse(data);
            string hex_str = BitConverter.ToString(data).Replace("-", string.Empty);

            //коэффициент, на который умножается число, полученное со счетчика
            float COEFFICIENT = 1;
            switch (param)
            {
                case Params.ENERGY:
                    {
                        //коэффициент, согласно документации MBUS, после применения дает значение в Wh
                        //COEFFICIENT = (float)Math.Pow(10, 3);
                        //однако, счетчик показывает значения в KWh
                        COEFFICIENT = 1;
                        break;
                    }
                case Params.VOLUME_FLOW:
                    {
                        COEFFICIENT = (float)Math.Pow(10, -3);
                        break;
                    }
                case Params.TEMP_INP:
                case Params.TEMP_OUTP:
                case Params.TEMP_DIFF:
                    {
                        COEFFICIENT = (float)Math.Pow(10, -2);
                        break;
                    }
                default:
                    {
                        break;
                    }
            }

            if (!float.TryParse(hex_str, out value))
            {
                value = 0f;

                string mgs = String.Format("Ошибка преобразования параметра {0} во float, исходная строка: {1}", param.ToString(), hex_str);
                WriteToLog(mgs);

                return false;
            }
            else
            {
                value *= COEFFICIENT;
                return true;
            }
        }

        public bool getRecordValueByParam(Params param, out float value)
        {
            List<Record> records = new List<Record>();
            value = 0f;

            if (!GetRecordsList(out records))
            {
                WriteToLog("getRecordValueByParam: can't split records");
                return false;
            }

            float res_val = 0f;
            if (getRecordValueByParam(param, records, out res_val))
            {
                value = res_val;
                return true;
            }
            else
            {
                WriteToLog("getRecordValueByParam: can't getRecordValueByParam");
                return false;
            }
        }

        public bool GetRecordsList(out List<Record> records)
        {
            records = new List<Record>();

            List<byte> answerBytes = new List<byte>();
            if (!SendREQ_UD2(out answerBytes) || answerBytes.Count == 0)
            {
                WriteToLog("ReadSerialNumber: не получены байты ответа");
                return false;
            }

            if (!SplitRecords(answerBytes, ref records) || records.Count == 0)
            {
                WriteToLog("ReadSerialNumber: не удалось разделить запись");
                return false;
            }

            return true;
        }


        //возвращает true если установлен extension bit, позволяет опрелелить, есть ли DIFE/VIFE
        private bool hasExtension(byte b)
        {
            byte EXTENSION_BIT_MASK = Convert.ToByte("10000000", 2);
            int extensionBit = (b & EXTENSION_BIT_MASK) >> 7;
            if (extensionBit == 1)
                return true;
            else
                return false;
        }

        public bool SplitRecords(List<byte> recordsBytes, ref List<Record> recordsList)
        {
            recordsList = new List<Record>();
            if (recordsBytes.Count == 0) return false;

            bool doStop = false;
            int index = 0;

            //переберем записи
            while (!doStop)
            {
                Record tmpRec = new Record();
                tmpRec.DIFEs = new List<byte>();
                tmpRec.VIFEs = new List<byte>();
                tmpRec.dataBytes = new List<byte>();

                tmpRec.DIF = recordsBytes[index];

                //определим длину и тип данных
                int dataLength = getLengthAndTypeFromDIF(tmpRec.DIF, out tmpRec.recordType);

                if (hasExtension(tmpRec.DIF))
                {
                    //переход к байту DIFE
                    index++;
                    byte DIFE = recordsBytes[index];
                    tmpRec.DIFEs.Add(DIFE);

                    while (hasExtension(DIFE))
                    {
                        //перейдем к следующему DIFE
                        index++;
                        DIFE = recordsBytes[index];
                        tmpRec.DIFEs.Add(DIFE);
                    }
                }

                //переход к VIF
                index++;
                tmpRec.VIF = recordsBytes[index];

                //проверим на наличие специального VIF, после которого следует ASCII строка
                if (tmpRec.VIF == Convert.ToByte("11111100", 2))
                {
                    index++;
                    int str_length = recordsBytes[index];
                    index += str_length;
                }

                if (hasExtension(tmpRec.VIF))
                {
                    //переход к VIFE
                    index++;
                    byte VIFE = recordsBytes[index];
                    tmpRec.VIFEs.Add(VIFE);

                    while (hasExtension(VIFE))
                    {
                        //перейдем к следующему VIFE
                        index++;
                        VIFE = recordsBytes[index];
                        tmpRec.VIFEs.Add(VIFE);
                    }
                }

                //переход к первому байту данных
                index++;
                int dataCnt = 0;
                while (dataCnt < dataLength)
                {
                    tmpRec.dataBytes.Add(recordsBytes[index]);
                    index++;
                    dataCnt++;
                }

                recordsList.Add(tmpRec);
                if (index >= recordsBytes.Count - 1) doStop = true;
            }

            return true;
        }
        public bool SendREQ_UD2(out List<byte> recordsBytesList)
        {
            recordsBytesList = new List<byte>();

            /*данные проходящие по протоколу m-bus не нужно шифровать, а также не нужно
             применять отрицание для зарезервированных символов*/
            byte cmd = 0x7b;
            byte CS = (byte)(cmd + m_addr);

            byte[] cmdArr = { 0x10, cmd, m_addr, CS, 0x16 };
            byte[] inp = new byte[256];

            try
            {
                //режим, когда незнаем сколько байт нужно принять
                m_vport.WriteReadData(findPackageSign, cmdArr, ref inp, cmdArr.Length, -1);

                string answ_str = "";
                foreach (byte b in inp)
                    answ_str += Convert.ToString(b, 16) + " ";
                WriteToLog(answ_str);

                if (inp.Length < 6)
                {
                    WriteToLog("SendREQ_UD2: Длина корректного ответа не может быть меньше 5 байт: " + answ_str);
                    return false;
                }

                int firstAnswerByteIndex = -1;
                int byteCIndex = -1;
                //определим индекс первого байта С
                for (int i = 0; i < inp.Length; i++)
                {
                    int j = i + 3;
                    if (inp[i] == 0x68 && j < inp.Length && inp[j] == 0x68)
                    {
                        firstAnswerByteIndex = i;
                        byteCIndex = ++j;
                    }
                }

                if (firstAnswerByteIndex == -1)
                {
                    WriteToLog("SendREQ_UD2: не определено начало ответа 0x68, firstAnswerByteIndex: " + firstAnswerByteIndex.ToString());
                    return false;
                }

                //определим длину данных ответа
                byte dataLength = inp[firstAnswerByteIndex + 1];
                if (dataLength != inp[firstAnswerByteIndex + 2])
                {
                    WriteToLog("SendREQ_UD2: не определена длина данных L, dataLength");
                    return false;
                }


                byte C = inp[byteCIndex];
                byte A = inp[byteCIndex + 1]; //адрес прибора 
                byte CI = inp[byteCIndex + 2]; //тип ответа, если 72h то с переменной длиной

                if (CI != 0x72)
                {
                    WriteToLog("SendREQ_UD2: счетчик должен ответить сообщением с переменной длиной, CI = 0x72");
                    return false;
                }

                int firstFixedDataHeaderIndex = byteCIndex + 3;
                byte[] factoryNumberBytes = new byte[4];
                Array.Copy(inp, firstFixedDataHeaderIndex, factoryNumberBytes, 0, factoryNumberBytes.Length);
                Array.Reverse(factoryNumberBytes);
                //серийный номер полученный из заголовка может быть изменен, достовернее серийник, полученный из блока записей
                string factoryNumber = BitConverter.ToString(factoryNumberBytes);

                //12 байт - размер заголовка, индекс первого байта первой записи
                int firstRecordByteIndex = firstFixedDataHeaderIndex + 12;

                //байт окончания сообщения
                int lastByteIndex = byteCIndex + dataLength + 1;
                int byteCSIndex = byteCIndex + dataLength;
                if (inp[lastByteIndex] != 0x16)
                {
                    WriteToLog("SendREQ_UD2: не найден байт окончания сообщения 0х16");
                    return false;
                }

                //индекс последнего байта последнегй записи
                int lastRecordByteIndex = lastByteIndex - 2;

                //поместим байты записей в отдельный список
                for (int i = firstRecordByteIndex; i <= lastRecordByteIndex; i++)
                    recordsBytesList.Add(inp[i]);

                return true;
            }
            catch (Exception ex)
            {
                WriteToLog("SendREQ_UD2: " + ex.Message);
                return false;
            }
        }

        //Служебный метод, опрашивающий счетчик для всех элементов перечисления Params,
        //и возвращающих ответ в виде строки. Примняется в тестовой утилите драйвера elf.
        public bool GetAllValues(out string res)
        {
            res = "Ошибка";
            List<Record> records = new List<Record>();
            if (!GetRecordsList(out records))
            {
                WriteToLog("GetAllValues: can't split records");
                return false;
            }

            res = "";
            foreach (Params p in Enum.GetValues(typeof(Params)))
            {
                float val = -1f;
                string s = "false;";

                if (getRecordValueByParam(p, records, out val))
                    s = val.ToString();

                res += String.Format("{0}: {1}\n", p.ToString(), s);
            }

            return true;
        }

        #endregion


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

        //используется для вывода в лог
        public string current_secondary_id_str = "серийный номер не определен";
        //выделяет счетчик по серийнику и возвращает признак того что прибор на связи
        public bool SelectBySecondaryId(int factoryNumber)
        {
            current_secondary_id_str = factoryNumber.ToString();

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

        //сбрасывает выделение конкретного счптчика
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
                if (inp[readBytes - 1] == 0xE5)
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
        public bool UnselectAllMeters()
        {
            bool res = false;
            this.SND_NKE(ref res);

            return res;
        }


        /// <summary>
        /// Чтение списка текущих значений для драйвера теплоучет
        /// </summary>
        /// <param name="valDict"></param>
        /// <returns></returns>
        public bool ReadCurrentValues(List<int> paramCodes, out List<float> values)
        {
            values = new List<float>();
            List<Record> records = new List<Record>();
            List<byte> answerBytes = new List<byte>();

            if (!SendREQ_UD2(out answerBytes) || answerBytes.Count == 0)
            {
                WriteToLog("ReadCurrentValues: не получены байты ответа");
                return false;
            }

            if (!SplitRecords(answerBytes, ref records) || records.Count == 0)
            {
                WriteToLog("ReadCurrentValues: не удалось разделить запись");
                return false;
            }

            foreach (int p in paramCodes)
            {
                float tmpVal = -1f;
                values.Add(tmpVal);

                if (!Enum.IsDefined(typeof(Params), p))
                {
                    WriteToLog("ReadCurrentValues не удалось найти в перечислении paramCodes параметр " + p.ToString());
                    continue;
                }

                Params tmpP = (Params)p;

                //не путать с перегруженным аналогом
                if (!getRecordValueByParam(tmpP, records, out tmpVal))
                {
                    WriteToLog("ReadCurrentValues не удалось выполнить getRecordValueByParam для " + tmpP);
                    continue;
                }

                values[values.Count - 1] = tmpVal;
            }

            return true;
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

        #region Методы поддержки интерфейса, неиспользуемые

        public bool OpenLinkCanal()
        {
            /*
            bool confirmation = false;
            if (!SND_NKE(ref confirmation))
                return false;

            if (!confirmation) return false;

            return true;
             * */
            WriteToLog("OpenLinkCanal() предназначен для СО и не реализован для драйвера");
            return false;
        }

        public bool ReadMonthlyValues(DateTime dt, ushort param, ushort tarif, ref float recordValue)
        {
            WriteToLog("ReadMonthlyValues(DateTime dt, ushort param, ushort tarif, ref float recordValue) не реализован для драйвера");
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
            WriteToLog("ReadDailyValues(DateTime dt, ushort param, ushort tarif, ref float recordValue) не реализован для драйвера");
            return false;
        }

        /// <summary>
        /// Чтение текущих значений для СО по параметрам
        /// </summary>
        /// <param name="values">Возвращаемые данные</param>
        /// <returns></returns>
        public bool ReadCurrentValues(ushort param, ushort tarif, ref float recordValue)
        {
            WriteToLog("ReadCurrentValues(ushort param, ushort tarif, ref float recordValue) не реализован для драйвера");
            return false;
        }

        int findPackageSign(Queue<byte> queue)
        {
            return 0;
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

        #endregion
    }




}
