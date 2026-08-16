import { describe, expect, it } from 'vitest';
import { toCsvString } from './exportCsv';

describe('toCsvString', () => {
  it('builds a header row from column definitions', () => {
    const csv = toCsvString([], [{ key: 'pid', header: 'PID' }, { key: 'name', header: 'Nombre' }]);
    expect(csv).toBe('PID,Nombre');
  });

  it('joins row values in column order', () => {
    const csv = toCsvString(
      [{ pid: 123, name: 'explorer.exe' }],
      [{ key: 'pid', header: 'PID' }, { key: 'name', header: 'Nombre' }]
    );
    expect(csv).toBe('PID,Nombre\r\n123,explorer.exe');
  });

  it('quotes fields containing commas, quotes or newlines', () => {
    const csv = toCsvString(
      [{ cmd: 'echo "hi", there\nbye' }],
      [{ key: 'cmd', header: 'CommandLine' }]
    );
    expect(csv).toBe('CommandLine\r\n"echo ""hi"", there\nbye"');
  });

  it('renders null/undefined values as empty fields', () => {
    const csv = toCsvString(
      [{ value: null as unknown as string }],
      [{ key: 'value', header: 'Value' }]
    );
    expect(csv).toBe('Value\r\n');
  });
});
