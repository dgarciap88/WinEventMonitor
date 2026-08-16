# WinEventMonitor

Este documento resume el estado actual del proyecto y sirve como guía rápida para mantenimiento, empaquetado, instalación y pruebas.

## Resumen del proyecto

WinEventMonitor es una herramienta de monitorización de eventos de seguridad de Windows en tiempo real. El objetivo es capturar, almacenar y visualizar actividad relevante del sistema para facilitar investigación, detección y respuesta.

La solución se compone de tres partes principales:

- WinEventMonitor.Service: backend .NET 9 que ejecuta la ingesta de eventos, expone una API local y persiste datos en SQLite.
- WinEventMonitor.Tray: aplicación WPF con WebView2 que actúa como bandeja del sistema y ventana principal.
- WinEventMonitor.Web: frontend React + TypeScript + Vite + Tailwind que se compila a contenido estático para el backend.

## Estado actual

El proyecto ya está convertido en una aplicación instalable de Windows y el flujo principal funciona de extremo a extremo.

Estado funcional actual:

- El backend corre como servicio de Windows.
- La API escucha en loopback local, en el puerto 51847 por defecto.
- La autenticación de la API se hace con una clave local en X-Api-Key.
- La app Tray no necesita elevación para abrirse y mostrar la interfaz.
- El instalador registra el servicio, crea accesos directos y añade el arranque automático de la Tray para el usuario actual.
- El frontend se sirve como contenido estático empaquetado dentro del publish.
- El árbol de procesos histórico, las alertas, la línea temporal por proceso y las integraciones con VirusTotal y MITRE ATT&CK están implementadas.

Notas importantes:

- El instalador actual se genera como un Setup.exe con Inno Setup. No se está produciendo un MSI nativo en este flujo.
- Si alguien dice MSI en este contexto, realmente se refiere al instalador generado por Inno Setup.

## Arquitectura actual

### Backend

Responsabilidades principales:

- Ingesta de eventos de Windows Security y Sysmon.
- Detección de patrones sospechosos.
- Persistencia en SQLite con Entity Framework Core.
- Exposición de endpoints para procesos, alertas, red, DNS, logons, timeline y lookup de VirusTotal.
- Gestión de configuración local y clave API.

### Tray

Responsabilidades principales:

- Arranque de la interfaz de usuario desde el publish local.
- Integración con WebView2.
- Experiencia de bandeja del sistema.
- Inyección automática de la cabecera X-Api-Key para consumir la API local.

### Frontend

Responsabilidades principales:

- Paneles de eventos, procesos, red, alertas y timeline.
- Búsqueda global.
- Visualización de salud del sistema.
- Interacción con la API local del servicio.

## Requisitos de desarrollo

- Windows 10/11 x64.
- .NET 9 SDK.
- Node.js 20 o superior.
- Inno Setup 6 para generar el instalador.
- Sysmon es opcional, pero muy recomendable para sacar más valor de la detección.

## Cómo generar el instalador

El punto de entrada recomendado es el script raíz build.ps1.

Desde la raíz del repositorio:

```powershell
.\build.ps1
```

El script hace, en este orden:

1. Limpia la carpeta publish.
2. Compila el frontend React/Vite.
3. Publica WinEventMonitor.Service como aplicación self-contained win-x64.
4. Publica WinEventMonitor.Tray como aplicación self-contained win-x64.
5. Verifica que wwwroot quedó incluido en publish.
6. Lanza Inno Setup para generar el instalador.

Resultado esperado:

- Carpeta de publicación: publish
- Instalador generado: installer/Output/WinEventMonitor-1.0.0-Setup.exe

### Generación solo de publish, sin instalador

Si solo quieres compilar y publicar sin usar Inno Setup:

```powershell
.\build.ps1 -SkipInno
```

Eso deja listo el contenido en publish para pruebas manuales o empaquetado posterior.

### Compilación manual del instalador

Si ya existe publish y solo quieres reconstruir el setup:

```powershell
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\installer\WinEventMonitor.iss /DMyAppVersion=1.0.0 /DPublishDir=..\publish
```

Si Inno Setup está instalado en otra ruta, usa la ubicación real de ISCC.exe.

## Cómo se instala

El instalador debe ejecutarse con privilegios de administrador, porque registra y arranca un servicio de Windows.

Durante la instalación se hace lo siguiente:

- Copia los binarios a Program Files.
- Registra el servicio WinEventMonitor.
- Configura el servicio para inicio automático.
- Crea acceso directo en el menú Inicio.
- Opcionalmente crea icono en el escritorio.
- Añade el arranque automático de la Tray para el usuario actual.

Datos y configuración persistente:

- La base de datos y la configuración viven en C:\ProgramData\WinEventMonitor.
- La clave API local también se guarda allí.
- En una actualización normal, los datos se conservan.

### Desinstalación

La desinstalación detiene y elimina el servicio. Además, pregunta si quieres borrar los datos persistentes de ProgramData.

## Cómo se prueba

### 1. Prueba de build

Ejecutar el pipeline completo:

```powershell
.\build.ps1
```

Validar que termina sin errores y que se genera el setup.

### 2. Prueba de frontend

Desde WinEventMonitor.Web:

```powershell
npm install
npm run build
```

Debe producirse la compilación estática usada por el backend y por la Tray.

### 3. Prueba del backend

Ejecutar el servicio en desarrollo o usar el binario publicado y comprobar:

- que el servicio arranca,
- que la API responde en localhost,
- que se crean o actualizan correctamente la base de datos y la clave API,
- que los eventos aparecen en la interfaz.

### 4. Prueba de la Tray

Abrir WinEventMonitor.Tray y verificar:

- que WebView2 inicia correctamente,
- que la UI carga desde el publish local,
- que la navegación a la API local funciona,
- que no aparece el prompt de elevación para la propia Tray.

### 5. Prueba de instalación real

Instalar el Setup.exe en una máquina limpia o una VM y confirmar:

- servicio registrado,
- arranque automático correcto,
- Tray visible al iniciar sesión,
- acceso a la UI,
- persistencia de datos tras reinstalar sobre una versión anterior.

## Pruebas recomendadas antes de cerrar cambios

Antes de dar por buena una versión, conviene validar al menos lo siguiente:

- build del frontend,
- dotnet publish de backend y Tray,
- compilación del instalador,
- arranque del servicio,
- apertura de la Tray,
- carga de la UI,
- lectura de eventos y renderizado básico de tablas y paneles.

## Privilegios

Estado deseado y actual:

- La app Tray no debe requerir privilegios de administrador.
- El servicio y la instalación sí requieren elevación.
- El usuario normal debe poder abrir la interfaz y usar la herramienta sin UAC extra una vez instalada.

## Archivos clave

- build.ps1: orquesta build, publish e instalador.
- installer/WinEventMonitor.iss: script de Inno Setup.
- WinEventMonitor.Service/Program.cs: arranque del backend y endpoints.
- WinEventMonitor.Tray/MainWindow.xaml.cs: carga de la UI y WebView2.
- WinEventMonitor.Web/src: frontend React.

## Observaciones operativas

- Si cambias el frontend, vuelve a ejecutar build.ps1 para regenerar publish.
- Si cambias el instalador o el comportamiento de arranque, recompila con Inno Setup.
- Si cambias la API local o la clave, revisa también la Tray porque inyecta la cabecera automáticamente.
- Si la UI no carga, revisa primero la carpeta publish y luego los logs de la Tray.

## Criterio de soporte

La referencia de funcionamiento correcta para esta versión es:

- servicio activo,
- API local respondiendo,
- Tray abierta sin elevar privilegios,
- paneles de la UI visibles,
- eventos y alertas cargando desde la base de datos local.

## Logs y diagnóstico

Cuando algo falla, revisa primero estas fuentes en este orden:

### 1. Logs de la Tray

Ruta:

- C:\Users\<usuario>\AppData\Local\WinEventMonitor\logs\tray-*.log

Qué indica:

- errores de inicio de WebView2,
- fallos de navegación de la UI,
- problemas al leer la clave API local,
- fallos al conectar con el servicio.

Mensajes útiles a buscar:

- Tray iniciado
- Inicializando WebView2 en puerto
- API Key presente
- wwwroot local encontrado
- Navegacion correcta a http://localhost:51847
- Error al inicializar WebView2
- CannotConnect

### 2. Logs del servicio

Ruta:

- C:\ProgramData\WinEventMonitor\logs\service-*.log

Qué indica:

- errores de arranque del backend,
- problemas de base de datos o migraciones,
- errores al enganchar Event Log o Sysmon,
- fallos de API o workers,
- problemas de retención o reglas de alertas.

### 3. Base de datos y configuración

Revisar si existe y si tiene permisos correctos:

- C:\ProgramData\WinEventMonitor\events.db
- C:\ProgramData\WinEventMonitor\api.key

Si la app no arranca o no muestra datos, comprobar que estos ficheros existen y que el proceso del servicio puede leerlos y escribirlos.

### 4. Visor de eventos de Windows

Si el problema es que el servicio no arranca, se detiene al instalar o falla al registrarse, revisar Event Viewer de Windows, especialmente:

- Windows Logs > Application
- Windows Logs > System

Qué buscar:

- errores del Service Control Manager,
- fallos de inicio del servicio WinEventMonitor,
- errores de permisos,
- problemas con dependencias del runtime,
- errores del instalador o del arranque automático.

### 5. Comprobación rápida por consola

Si quieres validar si el servicio está vivo sin abrir la UI:

```powershell
sc query WinEventMonitor
```

Si está detenido, revisar el log del servicio y el Visor de eventos.

### 6. Síntomas típicos y lectura rápida

- Si la Tray abre pero la UI no carga, mirar primero el log de la Tray.
- Si la UI abre pero no hay datos, mirar el log del servicio y la base de datos.
- Si el instalador termina pero el servicio no arranca, mirar Event Viewer y `sc query`.
- Si hay una actualización y se rompen rutas o binarios, revisar que `publish` se generó de nuevo y que el instalador se recompiló.

### 7. Comandos rápidos para abrir logs

```powershell
# Logs de la Tray
explorer "$env:LOCALAPPDATA\WinEventMonitor\logs"

# Logs del servicio
explorer "$env:ProgramData\WinEventMonitor\logs"

# Ver el último log de la Tray en consola
Get-ChildItem "$env:LOCALAPPDATA\WinEventMonitor\logs\tray-*.log" |
	Sort-Object LastWriteTime -Descending |
	Select-Object -First 1 |
	ForEach-Object { Get-Content $_.FullName -Tail 200 }

# Ver el último log del servicio en consola
Get-ChildItem "$env:ProgramData\WinEventMonitor\logs\service-*.log" |
	Sort-Object LastWriteTime -Descending |
	Select-Object -First 1 |
	ForEach-Object { Get-Content $_.FullName -Tail 200 }

# Comprobar estado del servicio
sc query WinEventMonitor

# Abrir el Visor de eventos de Windows
eventvwr.msc
```

## Mantenimiento recomendado

Antes de abrir una incidencia o tocar código, intenta dejar anotado:

- hora aproximada del fallo,
- último mensaje relevante del log,
- si el problema ocurre en la Tray, en el servicio o en la instalación,
- si el fallo ocurre solo tras instalar o también en ejecución normal.