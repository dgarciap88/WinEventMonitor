namespace WinEventMonitor.Service.Models;

/// <summary>
/// Puertos TCP en escucha ya vistos en este equipo. Sirve de base de referencia
/// para la regla "Nuevo puerto en escucha": la primera ejecucion solo aprende
/// los puertos actuales sin alertar, las siguientes alertan sobre los realmente nuevos.
/// </summary>
public class KnownListenPort
{
    public int Port { get; set; }
    public DateTime FirstSeen { get; set; }
}
