namespace Nmw.Core.Simulator;

/// <summary>
/// 시뮬레이터 공용 데이터 저장소 + Modbus 요청 처리기 (스레드 안전).
/// TCP/RTU 슬레이브 시뮬레이터가 같은 저장소를 공유할 수 있다.
/// FC01~06, 15, 16을 지원하고 범위 밖 주소는 Illegal Data Address(0x02),
/// 잘못된 개수/값은 Illegal Data Value(0x03), 미지원 FC는 Illegal Function(0x01)을 반환한다.
/// </summary>
public sealed class SimulatorDataStore
{
    private readonly object _gate = new();
    private readonly ushort[] _holding;
    private readonly ushort[] _input;
    private readonly bool[] _coils;
    private readonly bool[] _discrete;

    /// <summary>영역 크기로 저장소를 만든다.</summary>
    /// <param name="areaSize">각 데이터 영역의 주소 개수 (1..65536).</param>
    public SimulatorDataStore(int areaSize = 1000)
    {
        if (areaSize is < 1 or > 65536)
        {
            throw new ArgumentOutOfRangeException(
                nameof(areaSize), areaSize, "영역 크기는 1..65536 범위여야 합니다.");
        }

        AreaSize = areaSize;
        _holding = new ushort[areaSize];
        _input = new ushort[areaSize];
        _coils = new bool[areaSize];
        _discrete = new bool[areaSize];
    }

    /// <summary>각 데이터 영역의 주소 개수.</summary>
    public int AreaSize { get; }

    /// <summary>마스터의 쓰기 요청(FC05/06/15/16)으로 데이터가 바뀔 때 발생 (워커 스레드).</summary>
    public event EventHandler? DataChangedByMaster;

    /// <summary>홀딩 레지스터 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public ushort GetHoldingRegister(int address)
    {
        lock (_gate)
        {
            return _holding[CheckAddress(address)];
        }
    }

    /// <summary>홀딩 레지스터 값을 쓴다.</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetHoldingRegister(int address, ushort value)
    {
        lock (_gate)
        {
            _holding[CheckAddress(address)] = value;
        }
    }

    /// <summary>입력 레지스터 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public ushort GetInputRegister(int address)
    {
        lock (_gate)
        {
            return _input[CheckAddress(address)];
        }
    }

    /// <summary>입력 레지스터 값을 쓴다 (시뮬레이터 전용 — 마스터는 읽기만 가능).</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetInputRegister(int address, ushort value)
    {
        lock (_gate)
        {
            _input[CheckAddress(address)] = value;
        }
    }

    /// <summary>코일 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public bool GetCoil(int address)
    {
        lock (_gate)
        {
            return _coils[CheckAddress(address)];
        }
    }

    /// <summary>코일 값을 쓴다.</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetCoil(int address, bool value)
    {
        lock (_gate)
        {
            _coils[CheckAddress(address)] = value;
        }
    }

    /// <summary>접점 값을 읽는다.</summary>
    /// <param name="address">주소 (0-base).</param>
    public bool GetDiscreteInput(int address)
    {
        lock (_gate)
        {
            return _discrete[CheckAddress(address)];
        }
    }

    /// <summary>접점 값을 쓴다 (시뮬레이터 전용 — 마스터는 읽기만 가능).</summary>
    /// <param name="address">주소 (0-base).</param>
    /// <param name="value">값.</param>
    public void SetDiscreteInput(int address, bool value)
    {
        lock (_gate)
        {
            _discrete[CheckAddress(address)] = value;
        }
    }

    /// <summary>홀딩·입력 레지스터 주소 0..count-1 값을 1씩 증가시킨다 (값 자동 변화용, 래핑).</summary>
    /// <param name="count">증가시킬 주소 개수.</param>
    public void IncrementRegisters(int count) => IncrementRegisters(0, count);

    /// <summary>
    /// 홀딩·입력 레지스터의 지정 구간(startAddress..startAddress+count-1) 값을 1씩 증가시킨다
    /// (값 자동 변화용, 래핑, 영역 밖은 무시).
    /// </summary>
    /// <param name="startAddress">시작 주소 (0-base).</param>
    /// <param name="count">증가시킬 주소 개수.</param>
    public void IncrementRegisters(int startAddress, int count)
    {
        lock (_gate)
        {
            var from = Math.Clamp(startAddress, 0, AreaSize);
            var to = Math.Clamp(startAddress + count, from, AreaSize);
            for (var i = from; i < to; i++)
            {
                _holding[i] = unchecked((ushort)(_holding[i] + 1));
                _input[i] = unchecked((ushort)(_input[i] + 1));
            }
        }
    }

    /// <summary>요청 PDU를 처리해 응답 PDU를 만든다. 쓰기로 데이터가 바뀌면 이벤트를 발행한다.</summary>
    /// <param name="pdu">요청 PDU.</param>
    /// <returns>응답 PDU (정상 또는 exception).</returns>
    public byte[] ProcessPdu(byte[] pdu)
    {
        byte[] response;
        bool dataChanged;
        lock (_gate)
        {
            response = ProcessPduLocked(pdu, out dataChanged);
        }

        if (dataChanged)
        {
            DataChangedByMaster?.Invoke(this, EventArgs.Empty);
        }

        return response;
    }

    private int CheckAddress(int address) =>
        address >= 0 && address < AreaSize
            ? address
            : throw new ArgumentOutOfRangeException(
                nameof(address), address, $"주소는 0..{AreaSize - 1} 범위여야 합니다.");

    private static byte[] ExceptionPdu(byte functionCode, byte exceptionCode) =>
        [(byte)(functionCode | 0x80), exceptionCode];

    private static ushort ReadU16(byte[] pdu, int offset) =>
        (ushort)((pdu[offset] << 8) | pdu[offset + 1]);

    private byte[] ProcessPduLocked(byte[] pdu, out bool dataChanged)
    {
        dataChanged = false;
        if (pdu.Length < 1)
        {
            return ExceptionPdu(0, 0x01);
        }

        var fc = pdu[0];
        switch (fc)
        {
            case 0x01:
                return ReadBits(pdu, _coils);
            case 0x02:
                return ReadBits(pdu, _discrete);
            case 0x03:
                return ReadRegisters(pdu, _holding);
            case 0x04:
                return ReadRegisters(pdu, _input);
            case 0x05:
                return WriteSingleCoil(pdu, out dataChanged);
            case 0x06:
                return WriteSingleRegister(pdu, out dataChanged);
            case 0x0F:
                return WriteMultipleCoils(pdu, out dataChanged);
            case 0x10:
                return WriteMultipleRegisters(pdu, out dataChanged);
            default:
                return ExceptionPdu(fc, 0x01);
        }
    }

    private static byte[] ReadBits(byte[] pdu, bool[] map)
    {
        var fc = pdu[0];
        if (pdu.Length != 5)
        {
            return ExceptionPdu(fc, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var quantity = ReadU16(pdu, 3);
        if (quantity is < 1 or > 2000)
        {
            return ExceptionPdu(fc, 0x03);
        }

        if (address + quantity > map.Length)
        {
            return ExceptionPdu(fc, 0x02);
        }

        var byteCount = (byte)((quantity + 7) / 8);
        var response = new byte[2 + byteCount];
        response[0] = fc;
        response[1] = byteCount;
        for (var i = 0; i < quantity; i++)
        {
            if (map[address + i])
            {
                response[2 + (i / 8)] |= (byte)(1 << (i % 8));
            }
        }

        return response;
    }

    private static byte[] ReadRegisters(byte[] pdu, ushort[] map)
    {
        var fc = pdu[0];
        if (pdu.Length != 5)
        {
            return ExceptionPdu(fc, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var quantity = ReadU16(pdu, 3);
        if (quantity is < 1 or > 125)
        {
            return ExceptionPdu(fc, 0x03);
        }

        if (address + quantity > map.Length)
        {
            return ExceptionPdu(fc, 0x02);
        }

        var response = new byte[2 + (quantity * 2)];
        response[0] = fc;
        response[1] = (byte)(quantity * 2);
        for (var i = 0; i < quantity; i++)
        {
            response[2 + (i * 2)] = (byte)(map[address + i] >> 8);
            response[3 + (i * 2)] = (byte)map[address + i];
        }

        return response;
    }

    private byte[] WriteSingleCoil(byte[] pdu, out bool dataChanged)
    {
        dataChanged = false;
        if (pdu.Length != 5)
        {
            return ExceptionPdu(0x05, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var value = ReadU16(pdu, 3);
        if (value is not (0xFF00 or 0x0000))
        {
            return ExceptionPdu(0x05, 0x03);
        }

        if (address >= _coils.Length)
        {
            return ExceptionPdu(0x05, 0x02);
        }

        _coils[address] = value == 0xFF00;
        dataChanged = true;
        return pdu; // 요청 에코
    }

    private byte[] WriteSingleRegister(byte[] pdu, out bool dataChanged)
    {
        dataChanged = false;
        if (pdu.Length != 5)
        {
            return ExceptionPdu(0x06, 0x03);
        }

        var address = ReadU16(pdu, 1);
        if (address >= _holding.Length)
        {
            return ExceptionPdu(0x06, 0x02);
        }

        _holding[address] = ReadU16(pdu, 3);
        dataChanged = true;
        return pdu; // 요청 에코
    }

    private byte[] WriteMultipleCoils(byte[] pdu, out bool dataChanged)
    {
        dataChanged = false;
        if (pdu.Length < 6)
        {
            return ExceptionPdu(0x0F, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var quantity = ReadU16(pdu, 3);
        var byteCount = pdu[5];
        if (quantity is < 1 or > 1968 || byteCount != (quantity + 7) / 8 ||
            pdu.Length != 6 + byteCount)
        {
            return ExceptionPdu(0x0F, 0x03);
        }

        if (address + quantity > _coils.Length)
        {
            return ExceptionPdu(0x0F, 0x02);
        }

        for (var i = 0; i < quantity; i++)
        {
            _coils[address + i] = (pdu[6 + (i / 8)] & (1 << (i % 8))) != 0;
        }

        dataChanged = true;
        return [0x0F, pdu[1], pdu[2], pdu[3], pdu[4]];
    }

    private byte[] WriteMultipleRegisters(byte[] pdu, out bool dataChanged)
    {
        dataChanged = false;
        if (pdu.Length < 6)
        {
            return ExceptionPdu(0x10, 0x03);
        }

        var address = ReadU16(pdu, 1);
        var quantity = ReadU16(pdu, 3);
        var byteCount = pdu[5];
        if (quantity is < 1 or > 123 || byteCount != quantity * 2 ||
            pdu.Length != 6 + byteCount)
        {
            return ExceptionPdu(0x10, 0x03);
        }

        if (address + quantity > _holding.Length)
        {
            return ExceptionPdu(0x10, 0x02);
        }

        for (var i = 0; i < quantity; i++)
        {
            _holding[address + i] = ReadU16(pdu, 6 + (i * 2));
        }

        dataChanged = true;
        return [0x10, pdu[1], pdu[2], pdu[3], pdu[4]];
    }
}
