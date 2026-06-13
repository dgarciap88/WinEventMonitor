import { format } from 'date-fns';

interface Props {
  value: string;
}

export function Timestamp({ value }: Props) {
  const date = new Date(value);
  return (
    <span className="text-xs text-gray-500 whitespace-nowrap">
      {format(date, 'dd/MM/yyyy HH:mm:ss')}
    </span>
  );
}
