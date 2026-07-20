namespace Nmw.Core.Protocol;

/// <summary>지원하는 Modbus function code.</summary>
public enum FunctionCode : byte
{
    /// <summary>FC01 — Read Coils.</summary>
    ReadCoils = 0x01,

    /// <summary>FC02 — Read Discrete Inputs.</summary>
    ReadDiscreteInputs = 0x02,

    /// <summary>FC03 — Read Holding Registers.</summary>
    ReadHoldingRegisters = 0x03,

    /// <summary>FC04 — Read Input Registers.</summary>
    ReadInputRegisters = 0x04,

    /// <summary>FC05 — Write Single Coil.</summary>
    WriteSingleCoil = 0x05,

    /// <summary>FC06 — Write Single Register.</summary>
    WriteSingleRegister = 0x06,

    /// <summary>FC15 (0x0F) — Write Multiple Coils.</summary>
    WriteMultipleCoils = 0x0F,

    /// <summary>FC16 (0x10) — Write Multiple Registers.</summary>
    WriteMultipleRegisters = 0x10,
}
