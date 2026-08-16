import { describe, expect, it } from 'vitest';
import { getAlertExplanation } from './alertExplanations';

// Debe coincidir EXACTAMENTE con los strings Rule = "..." de AlertWorker.cs
const KNOWN_RULES = [
  'LOLBin – Shell desde app de documentos',
  'PowerShell Encoded',
  'Proceso desde ruta sospechosa',
  'DNS – TLD sospechoso',
  'Movimiento lateral – Puerto de administración',
  'Reverse Shell – Shell con conexión de red',
  'Fuerza bruta – Logon fallido repetido',
  'RDP desde IP nueva',
  'Script Host – Hijo de wscript/cscript',
  'PowerShell Dropper',
  'Ejecución desde UNC Path',
  'Eliminación de Shadow Copies',
  'LOLBin – Proxy de confianza',
  'DLL sin firma cargada',
  'CreateRemoteThread – Inyección de hilo',
  'Acceso a LSASS – Volcado de credenciales',
  'Persistencia – Clave de autoarranque',
  'Persistencia – Tarea programada nueva',
  'Persistencia – Servicio de Windows nuevo',
  'Cuenta – Usuario o administrador nuevo',
  'Auditoría – Registro de seguridad borrado',
];

describe('getAlertExplanation', () => {
  it.each(KNOWN_RULES)('has an explanation for "%s"', rule => {
    const explanation = getAlertExplanation(rule);
    expect(explanation).not.toBeNull();
    expect(explanation!.summary.length).toBeGreaterThan(0);
    expect(explanation!.action.length).toBeGreaterThan(0);
  });

  it('returns null for an unknown rule', () => {
    expect(getAlertExplanation('Regla inventada que no existe')).toBeNull();
  });
});
