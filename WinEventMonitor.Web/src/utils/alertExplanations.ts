/**
 * Explicación en lenguaje llano y acción recomendada para cada regla de
 * detección. La clave debe coincidir EXACTAMENTE con el campo `rule` que
 * genera WinEventMonitor.Service/Workers/AlertWorker.cs.
 */
export interface AlertExplanation {
  summary: string;
  action: string;
}

const EXPLANATIONS: Record<string, AlertExplanation> = {
  'LOLBin – Shell desde app de documentos': {
    summary:
      'Un programa como Word, Excel o el navegador ha abierto una consola de comandos. Esto no ocurre en un uso normal — suele ser la señal de que un documento o una macro maliciosa intentó ejecutar código.',
    action:
      'Revisa qué documento tenías abierto justo antes. Si no reconoces la acción, no vuelvas a abrir ese archivo y pasa un antivirus.',
  },
  'PowerShell Encoded': {
    summary:
      'Se ha ejecutado PowerShell con un comando "codificado" (ilegible a simple vista). Es una técnica muy usada para ocultar lo que realmente hace un script.',
    action:
      'Si no reconoces la aplicación que lo lanzó, investígala. Si es una herramienta de administración que usas habitualmente, puedes marcar la alerta como confiable.',
  },
  'Proceso desde ruta sospechosa': {
    summary:
      'Un programa se ha ejecutado desde una carpeta de archivos temporales o descargas, no desde donde suelen instalarse las aplicaciones. El malware se copia ahí porque cualquier usuario puede escribir en esas carpetas.',
    action:
      'Si lo acabas de descargar tú mismo, es probablemente normal. Si no reconoces el programa, no lo vuelvas a ejecutar.',
  },
  'DNS – TLD sospechoso': {
    summary:
      'Tu equipo ha intentado resolver un dominio con una terminación (.xyz, .tk, .onion…) usada mucho más a menudo para infraestructura maliciosa que para sitios legítimos.',
    action:
      'Comprueba qué aplicación hizo la consulta. Puede ser publicidad inofensiva, pero conviene confirmarlo si no la reconoces.',
  },
  'Movimiento lateral – Puerto de administración': {
    summary:
      'Un programa se ha conectado a un puerto de administración remota (compartir archivos, escritorio remoto, gestión de sistemas) de otro equipo de tu red. Es lo que hace un atacante para moverse entre ordenadores.',
    action:
      'Si es tu propio uso habitual (copiar archivos, acceder a otro PC), es normal. Si no lo reconoces, comprueba a qué equipo se conectó.',
  },
  'Reverse Shell – Shell con conexión de red': {
    summary:
      'Una consola de comandos ha hecho una conexión de red justo después de abrirse. Es el patrón típico de un atacante remoto tomando el control del equipo a través de la consola.',
    action:
      'Alerta de alta prioridad. Si no eres tú usando esa consola para conectarte a algo, considera desconectar el equipo de la red y revisar el proceso.',
  },
  'Fuerza bruta – Logon fallido repetido': {
    summary:
      'Alguien ha intentado iniciar sesión varias veces seguidas sin éxito desde la misma dirección de red en muy poco tiempo — el patrón típico de un intento automatizado de adivinar una contraseña.',
    action:
      'Si no reconoces esa IP, cambia la contraseña de la cuenta afectada y revisa si el acceso remoto es realmente necesario.',
  },
  'RDP desde IP nueva': {
    summary:
      'Alguien ha entrado por Escritorio Remoto desde una dirección que no se había visto en los últimos 7 días.',
    action:
      'Si no fuiste tú desde una ubicación nueva, trátalo como un acceso no autorizado: cambia la contraseña y revisa qué se hizo en esa sesión.',
  },
  'Script Host – Hijo de wscript/cscript': {
    summary:
      'Un script de Windows (VBScript/JScript) ha lanzado otro programa — una técnica muy común para ejecutar malware camuflado como un documento o adjunto de correo.',
    action:
      'Revisa de dónde vino ese script (¿un correo? ¿una descarga?). Si no lo reconoces, no lo vuelvas a abrir.',
  },
  'PowerShell Dropper': {
    summary:
      'PowerShell se ha ejecutado con instrucciones para descargar o ejecutar código desde internet al momento — la forma más común de instalar malware con un solo comando.',
    action:
      'Comprueba qué proceso lo lanzó. Si es una herramienta de instalación que reconoces, puedes marcarla como confiable.',
  },
  'Ejecución desde UNC Path': {
    summary:
      'Se ha ejecutado un programa directamente desde otro equipo de la red en lugar de copiarlo primero a tu disco — una forma habitual de propagar malware entre equipos de una misma red.',
    action:
      'Si no reconoces ese recurso compartido de red, investígalo antes de que se repita en otros equipos.',
  },
  'Eliminación de Shadow Copies': {
    summary:
      'Se ha intentado borrar las copias de seguridad automáticas de Windows — prácticamente la firma de un ataque de ransomware, que borra tu forma de recuperar archivos antes de cifrarlos.',
    action:
      'Alerta crítica. Si no lo has hecho tú intencionadamente, desconecta el equipo de la red y revisa el estado de tus archivos.',
  },
  'LOLBin – Proxy de confianza': {
    summary:
      'Un programa del propio Windows considerado "de confianza" ha sido usado para lanzar una consola de comandos. Los atacantes abusan de estos programas porque el antivirus tiende a confiar en ellos.',
    action:
      'Revisa qué se ejecutó realmente. Trátalo como sospechoso aunque el programa que lo originó sea de Windows.',
  },
  'DLL sin firma cargada': {
    summary:
      'Un proceso ha cargado un componente sin la firma digital que certifica quién lo creó. El software legítimo casi siempre está firmado; el malware, casi nunca.',
    action:
      'Si el proceso viene de una fuente que no sea la oficial, revisa su origen. Puedes comprobar el hash SHA256 en VirusTotal desde el detalle.',
  },
  'CreateRemoteThread – Inyección de hilo': {
    summary:
      'Un programa ha insertado código directamente dentro de otro proceso en ejecución — una técnica clásica para ocultar malware "dentro" de un programa legítimo y evitar así ser detectado.',
    action:
      'Alerta de alta prioridad. Si no reconoces esta relación entre los dos procesos, hay indicios de compromiso.',
  },
  'Acceso a LSASS – Volcado de credenciales': {
    summary:
      'Un proceso ha accedido a lsass.exe, el componente de Windows que guarda en memoria las contraseñas de las sesiones activas — la técnica más común para robar credenciales.',
    action:
      'Alerta crítica. Si no es una herramienta de seguridad que reconozcas, cambia tus contraseñas y revisa el equipo con más detalle.',
  },
  'Persistencia – Clave de autoarranque': {
    summary:
      'Se ha añadido un programa a una de las claves del registro que Windows ejecuta automáticamente al iniciar sesión — la forma más común de que un programa sobreviva a un reinicio.',
    action:
      'Revisa qué programa se añadió. Si no lo instalaste tú ni lo reconoces, elimínalo de esa clave y analiza el equipo.',
  },
  'Persistencia – Tarea programada nueva': {
    summary:
      'Se ha creado una tarea programada nueva, que puede ejecutar un programa automáticamente en el futuro (al iniciar sesión, a una hora concreta…) — otra forma habitual de conseguir persistencia.',
    action:
      'Revisa el Programador de tareas de Windows para ver qué tarea se creó y qué ejecuta. Si no la reconoces, elimínala.',
  },
  'Persistencia – Servicio de Windows nuevo': {
    summary:
      'Se ha registrado un nuevo servicio de Windows, que puede arrancar automáticamente con el sistema y ejecutarse con privilegios elevados.',
    action:
      'Revisa en services.msc qué servicio se creó y qué programa ejecuta. Si no lo reconoces, deshabilítalo.',
  },
  'Cuenta – Usuario o administrador nuevo': {
    summary:
      'Se ha creado una cuenta de usuario o se ha añadido alguien al grupo de Administradores de este equipo — control total sobre el sistema.',
    action:
      'Revisa las cuentas de usuario y el grupo Administradores. Si no reconoces el cambio, elimina la cuenta o quítala del grupo y cambia tus contraseñas.',
  },
  'Auditoría – Registro de seguridad borrado': {
    summary:
      'Alguien ha borrado el registro de eventos de seguridad de Windows — una de las técnicas más directas para ocultar lo que se hizo en el equipo justo antes.',
    action:
      'Alerta crítica: el borrado del log es sospechoso incluso si no ves nada más. Si no lo hiciste tú, investiga qué pudo haber pasado justo antes.',
  },
};

export function getAlertExplanation(rule: string): AlertExplanation | null {
  return EXPLANATIONS[rule] ?? null;
}
