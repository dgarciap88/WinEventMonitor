interface Props {
  elevated: boolean;
}

export function ElevatedBadge({ elevated }: Props) {
  return elevated ? (
    <span className="inline-block px-2 py-0.5 text-xs font-semibold rounded bg-red-100 text-red-700">
      Admin
    </span>
  ) : (
    <span className="inline-block px-2 py-0.5 text-xs rounded bg-gray-100 text-gray-500">
      Normal
    </span>
  );
}
