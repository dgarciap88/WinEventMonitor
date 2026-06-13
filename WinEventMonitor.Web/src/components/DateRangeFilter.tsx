interface Props {
  from: string;
  to: string;
  onFromChange: (v: string) => void;
  onToChange: (v: string) => void;
}

export function DateRangeFilter({ from, to, onFromChange, onToChange }: Props) {
  return (
    <>
      <div className="flex items-center gap-1">
        <label className="text-xs text-gray-500 whitespace-nowrap">Desde</label>
        <input
          type="datetime-local"
          className="border rounded px-2 py-1 text-sm"
          value={from}
          onChange={e => onFromChange(e.target.value)}
        />
      </div>
      <div className="flex items-center gap-1">
        <label className="text-xs text-gray-500 whitespace-nowrap">Hasta</label>
        <input
          type="datetime-local"
          className="border rounded px-2 py-1 text-sm"
          value={to}
          onChange={e => onToChange(e.target.value)}
        />
      </div>
    </>
  );
}
