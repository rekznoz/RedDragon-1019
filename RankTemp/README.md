# RankTemp

Aplicación en desarrollo para la refrigeración Redragon 1019 en Windows. El primer objetivo es identificar la unidad y reproducir el envío de telemetría a su pantalla.

## Aplicación de escritorio

Extrae `dist/RankTemp-Windows-x64.zip` en el PC del 1019 y abre `RankTemp.exe` con CpuTemp cerrado. La interfaz preselecciona el puerto cuando Windows expone un solo CH340 con el identificador del 1019. Puedes seleccionar el puerto manualmente si hay varios. La conexión se valida con el saludo antes de sincronizar.

Si ya tienes activado **Iniciar RankTemp con Windows**, sal de RankTemp desde la bandeja, extrae la nueva versión y ábrela. Pulsa **Actualizar copia de inicio** para que Windows use esa versión al iniciar sesión. RankTemp conserva el puerto y los sensores guardados.

RankTemp lee la temperatura de CPU y las RPM con LibreHardwareMonitor 0.9.6. La edición con PawnIO ofrece instalar el controlador en el primer arranque si hace falta. Después de aceptar el aviso de Windows, RankTemp instala el controlador en segundo plano y vuelve a abrirse. Si pospones la instalación, puedes retomarla con **Activar sensores**. [PawnIO 2.2.0](https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0)

Puedes elegir qué sensor corresponde al ventilador y a la bomba. La placa debe exponer las RPM de cada conexión para poder leerlas. Una temperatura de CPU de 0 °C se trata como una lectura ausente. Se envía `FFFF` cuando falta una lectura o está desmarcada, y en los dos campos finales, cuya función queda por verificar.

En el equipo probado con una ASRock B450M-HDV R4.0 y un Ryzen 7 5700X, la pantalla del 1019 mostró las tres lecturas en vivo. La asignación usada fue `Core (Tctl/Tdie)` para la temperatura, `Fan #1` del Nuvoton NCT6793D para el ventilador y `Fan #2` para la bomba. La asignación de RPM depende del cableado.

En Windows x64, ejecuta `.\Build-RankTemp.ps1` desde esta carpeta para generar el ZIP. Añade `-IncludeSensorDriver` para incluir PawnIO. El script comprueba el SHA-256 de las dependencias que empaqueta. [LibreHardwareMonitor 0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6)

## Identificar el 1019

En el PC donde esté conectada la refrigeración, ejecuta desde la carpeta del proyecto:

```powershell
.\RankTemp\Find-RankTempDevice.ps1
```

El script guarda el resultado en `RankTemp\RankTemp-diagnostico.txt`. Cada ejecución reemplaza el informe anterior, incluso si la consulta falla o no encuentra candidatos.

La herramienta enumera los dispositivos USB presentes con identificadores incluidos en el controlador CH341 de CpuTemp. Muestra el puerto COM cuando Windows lo incluye en el nombre del dispositivo. Los resultados son candidatos porque otros equipos también usan ese controlador. La consulta no abre el puerto ni envía comandos. [Documentación de Get-PnpDevice](https://learn.microsoft.com/en-us/powershell/module/pnpdevice/get-pnpdevice?view=windowsserver2025-ps)

## Telemetría del 1019

El diagnóstico en el PC del 1019 detectó un **USB-SERIAL CH340** con identificador **`1A86:7523`**, asignado a **`COM3`** y con estado **OK**. CpuTemp solicita **115 200 baudios**. Los demás parámetros de apertura quedan por verificar con una captura de arranque.

En `asd.pcapng`, el PC envió 52 mensajes de 12 bytes al endpoint USB `0x02`, aproximadamente uno por segundo. El endpoint `0x82` devolvió un byte `0x21` después de cada mensaje, en menos de 5 ms. Todos los mensajes empezaron por `01 0A`, seguidos de cinco palabras de 16 bits en *big-endian*.

| Bytes desde cero | Campo | Evidencia |
| --- | --- | --- |
| 0–1 | Cabecera `01 0A` | Constante en la captura. `0A` coincide con el tamaño de la carga útil. |
| 2–3 | Temperatura de CPU | Cambia a `FFFF` al ocultarla. Los valores encajan con décimas de °C. |
| 4–5 | Velocidad de la bomba | Cambia a `FFFF` al ocultarla. Los valores encajan con RPM. |
| 6–7 | Velocidad del ventilador de CPU | Cambia a `FFFF` al ocultarlo. Los valores encajan con RPM. |
| 8–9 y 10–11 | Dos valores pendientes | El bytecode menciona temperatura y ventilador de GPU. La captura por sí sola no confirma esa asignación. |

El cambio de cada casilla se refleja en el siguiente mensaje periódico. En esta muestra no se observó un mensaje separado para las casillas ni un delimitador o suma de comprobación adicional. Las referencias estáticas a `DataFrame` quedan como pista para otros mensajes.

`telemetry.cjs` codifica y decodifica estos mensajes sin abrir el puerto. Se verificó que reproduce los 52 mensajes byte por byte.

## Arranque

En `RankTemp-1019-arranque.pcapng`, CpuTemp abre el puerto, envía `02 01 00` y recibe `31` unos 3,9 ms después. El controlador vuelve a inicializar el CH340 unos 0,31 s después y CpuTemp empieza a enviar telemetría unos 2,08 s después de la consulta inicial. En ese segundo tramo, el primer mensaje de aplicación observado ya es una trama `01 0A`.

La configuración de línea observada es compatible con 8 bits de datos, sin paridad y un bit de parada. [Definiciones de línea del controlador CH341](https://linux.googlesource.com/linux/kernel/git/torvalds/linux/%2B/11e3b725cfc282efe9d4a354153e99d86a16af08/drivers/usb/serial/ch341.c)

Con CpuTemp cerrado, ejecuta la prueba desde la carpeta que contiene el script:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Test-RankTempConnection.ps1" -Port COM3
```

La prueba abre el puerto a 115 200 baudios con 8N1, envía una sola consulta de arranque y guarda el resultado en `RankTemp-conexion.txt` junto al script. La respuesta esperada es `RX 31`.

El 1019 confirmó ese saludo en `COM3`. Añade `-TestDisplay` para enviar después nueve tramas con datos grabados, una por segundo. La temperatura de ejemplo sigue la secuencia 60 °C, oculta, 60 °C.

La prueba en el 1019 devolvió `RX 31` al saludo y `RX 21` a las nueve tramas en la misma apertura. El usuario confirmó la secuencia de temperatura visible, oculta y visible en pantalla.
