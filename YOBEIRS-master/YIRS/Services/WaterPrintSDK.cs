using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using YIRS.Models;
using YIRS.Services;

namespace YIRS.Services
{
    public class WaterPrintSDK
    {
        private readonly BluetoothPrinterService _printerService;

        public WaterPrintSDK()
        {
            _printerService = new BluetoothPrinterService();
        }

      
        public async Task<bool> PrintTestAsync()
        {
            try
            {
                var payload = new List<byte>();
                payload.AddRange(ESCCommands.Initialize);
                payload.AddRange(ESCCommands.AlignCenter);

                var sb = new StringBuilder();
                sb.AppendLine("================================");
                sb.AppendLine("    YOBE STATE WATER CORP       ");
                sb.AppendLine("       TEST PRINT RECEIPT       ");
                sb.AppendLine("================================");
                sb.AppendLine($"Date: {DateTime.Now:dd-MMM-yyyy HH:mm}");
                sb.AppendLine("Status: PRINTER READY (OK)");
                sb.AppendLine("================================\n\n\n");

                payload.AddRange(Encoding.UTF8.GetBytes(sb.ToString()));
                return await _printerService.PrintRawBytesAsync(payload.ToArray());
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> PrintRegistrationReceiptAsync(WaterEnumerateResponse regData, WaterEnumerateRequest reqData, string areaName, string tariffName)
        {
            if (regData == null || reqData == null) return false;

            try
            {
                var payload = new List<byte>();
                payload.AddRange(ESCCommands.Initialize);
                payload.AddRange(ESCCommands.AlignCenter);

                var sb = new StringBuilder();
                sb.AppendLine("YOBE STATE WATER CORPORATION");
                sb.AppendLine("WATER CONSUMER ENUMERATION SLIP");
                sb.AppendLine("================================");
                sb.AppendLine($"Date/Time : {DateTime.Now:dd-MMM-yyyy HH:mm}");
                sb.AppendLine($"Area Post : {areaName}");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"CONN NO   : {regData.connectionNo}");
                sb.AppendLine($"OCCUPANT  : {regData.occupant}");
                sb.AppendLine($"PHONE     : {reqData.phone}");
                sb.AppendLine($"ADDRESS   : {reqData.address}");
                if (!string.IsNullOrWhiteSpace(reqData.flatNo))
                    sb.AppendLine($"FLAT/UNIT : {reqData.flatNo}");
                sb.AppendLine($"TARIFF    : {tariffName}");
                sb.AppendLine($"MONTH RATE: NGN {regData.amount:N2}");
                sb.AppendLine($"DUE DATE  : {regData.dueDate:dd-MMM-yyyy}");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"Agent     : {reqData.recordedBy}");
                sb.AppendLine("================================");
                sb.AppendLine("  KEEP THIS CONNECTION NUMBER   ");
                sb.AppendLine("  REQUIRED FOR ALL BILL PAYMENTS\n");

                payload.AddRange(Encoding.UTF8.GetBytes(sb.ToString()));
                payload.AddRange(ESCCommands.GetBarcode128Bytes(regData.connectionNo));
                payload.AddRange(new byte[] { 0x0A });

                string qrPayload = $"https://yobe.osoftpay.net/verify/water?conn={regData.connectionNo}";
                payload.AddRange(ESCCommands.GetQrCodeBytes(qrPayload, moduleSize: 4));

                var footerSb = new StringBuilder();
                footerSb.AppendLine("\n================================");
                footerSb.AppendLine("  Powered by YIRS Revenue Ops   ");
                footerSb.AppendLine("================================\n\n\n");
                payload.AddRange(Encoding.UTF8.GetBytes(footerSb.ToString()));

                return await _printerService.PrintRawBytesAsync(payload.ToArray());
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> PrintPaymentReceiptAsync(WaterReceiptResponse receipt, int monthsPaid)
        {
            if (receipt == null) return false;

            try
            {
                var payload = new List<byte>();
                payload.AddRange(ESCCommands.Initialize);
                payload.AddRange(ESCCommands.AlignCenter);

                var sb = new StringBuilder();
                sb.AppendLine("YOBE STATE WATER CORPORATION");
                sb.AppendLine("OFFICIAL WATER REVENUE RECEIPT");
                sb.AppendLine("================================");
                sb.AppendLine($"Receipt No : {receipt.transactionId}");
                sb.AppendLine($"Debit Ref  : {receipt.debitRef}");
                sb.AppendLine($"Date & Time: {receipt.datelIst:dd-MMM-yyyy HH:mm}");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"CONN NO    : {receipt.payer}");
                sb.AppendLine($"OCCUPANT   : {receipt.occupant}");
                sb.AppendLine($"ADDRESS    : {receipt.address}");
                sb.AppendLine($"MONTHS PAID: {monthsPaid} Month(s)");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"AMOUNT PAID: NGN {receipt.amount:N2}");
                sb.AppendLine("PAY STATUS : SUCCESSFUL (PAID)");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"Agent      : {receipt.performedBy}");
                sb.AppendLine("================================");
                sb.AppendLine("    SCAN TO VERIFY RECEIPT      \n");

                payload.AddRange(Encoding.UTF8.GetBytes(sb.ToString()));

                string qrPayload = $"https://yobe.osoftpay.net/receipt/verify?tx={receipt.transactionId}&conn={receipt.payer}";
                payload.AddRange(ESCCommands.GetQrCodeBytes(qrPayload, moduleSize: 4));

                var footerSb = new StringBuilder();
                footerSb.AppendLine("\n================================");
                footerSb.AppendLine("  Thank you for your payment!   ");
                footerSb.AppendLine("================================\n\n\n");
                payload.AddRange(Encoding.UTF8.GetBytes(footerSb.ToString()));

                return await _printerService.PrintRawBytesAsync(payload.ToArray());
            }
            catch (Exception)
            {
                return false;
            }
        }



        public static class ESCCommands
        {
            public static readonly byte[] Initialize = new byte[] { 0x1B, 0x40 };
            public static readonly byte[] AlignLeft = new byte[] { 0x1B, 0x61, 0x00 };
            public static readonly byte[] AlignCenter = new byte[] { 0x1B, 0x61, 0x01 };
            public static readonly byte[] AlignRight = new byte[] { 0x1B, 0x61, 0x02 };
            public static readonly byte[] BoldOn = new byte[] { 0x1B, 0x45, 0x01 };
            public static readonly byte[] BoldOff = new byte[] { 0x1B, 0x45, 0x00 };
            public static readonly byte[] FeedLines3 = new byte[] { 0x1B, 0x64, 0x03 };

            public static byte[] GetQrCodeBytes(string data, byte moduleSize = 4)
            {
                var bytes = new List<byte>();
                byte[] textBytes = Encoding.UTF8.GetBytes(data);
                int dataLength = textBytes.Length + 3;

                byte pL = (byte)(dataLength % 256);
                byte pH = (byte)(dataLength / 256);

                bytes.AddRange(AlignCenter);
                bytes.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 });
                bytes.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, moduleSize });
                bytes.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x31 });
                bytes.AddRange(new byte[] { 0x1D, 0x28, 0x6B, pL, pH, 0x31, 0x50, 0x30 });
                bytes.AddRange(textBytes);
                bytes.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 });
                bytes.AddRange(AlignLeft);

                return bytes.ToArray();
            }

            public static byte[] GetBarcode128Bytes(string code, byte height = 60)
            {
                var bytes = new List<byte>();
                byte[] codeBytes = Encoding.ASCII.GetBytes(code);

                bytes.AddRange(AlignCenter);
                bytes.AddRange(new byte[] { 0x1D, 0x68, height });
                bytes.AddRange(new byte[] { 0x1D, 0x77, 0x02 });
                bytes.AddRange(new byte[] { 0x1D, 0x48, 0x02 });
                bytes.AddRange(new byte[] { 0x1D, 0x6B, 0x49, (byte)codeBytes.Length });
                bytes.AddRange(codeBytes);
                bytes.AddRange(AlignLeft);

                return bytes.ToArray();
            }
        }
    }
}
