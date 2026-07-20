namespace Nmw.Core.Protocol;

/// <summary>Modbus exception 응답의 exception code.</summary>
public enum ModbusExceptionCode : byte
{
    /// <summary>0x01 — Illegal Function.</summary>
    IllegalFunction = 0x01,

    /// <summary>0x02 — Illegal Data Address.</summary>
    IllegalDataAddress = 0x02,

    /// <summary>0x03 — Illegal Data Value.</summary>
    IllegalDataValue = 0x03,

    /// <summary>0x04 — Slave Device Failure.</summary>
    SlaveDeviceFailure = 0x04,

    /// <summary>0x05 — Acknowledge.</summary>
    Acknowledge = 0x05,

    /// <summary>0x06 — Slave Device Busy.</summary>
    SlaveDeviceBusy = 0x06,

    /// <summary>0x08 — Memory Parity Error.</summary>
    MemoryParityError = 0x08,

    /// <summary>0x0A — Gateway Path Unavailable.</summary>
    GatewayPathUnavailable = 0x0A,

    /// <summary>0x0B — Gateway Target Failed to Respond.</summary>
    GatewayTargetFailedToRespond = 0x0B,
}

/// <summary><see cref="ModbusExceptionCode"/> 확장 메서드.</summary>
public static class ModbusExceptionCodeExtensions
{
    /// <summary>UI에 표시할 exception 명칭을 반환한다. 미정의 코드는 hex 표기로 반환한다.</summary>
    /// <param name="code">exception code.</param>
    /// <returns>표시 문자열.</returns>
    public static string GetDisplayName(this ModbusExceptionCode code) => code switch
    {
        ModbusExceptionCode.IllegalFunction => "Illegal Function",
        ModbusExceptionCode.IllegalDataAddress => "Illegal Data Address",
        ModbusExceptionCode.IllegalDataValue => "Illegal Data Value",
        ModbusExceptionCode.SlaveDeviceFailure => "Slave Device Failure",
        ModbusExceptionCode.Acknowledge => "Acknowledge",
        ModbusExceptionCode.SlaveDeviceBusy => "Slave Device Busy",
        ModbusExceptionCode.MemoryParityError => "Memory Parity Error",
        ModbusExceptionCode.GatewayPathUnavailable => "Gateway Path Unavailable",
        ModbusExceptionCode.GatewayTargetFailedToRespond => "Gateway Target Failed to Respond",
        _ => $"Unknown Exception (0x{(byte)code:X2})",
    };
}
