# Pizarra

Pizarra digital para Windows: dibuja encima de la pantalla, en hojas independientes o escribe con el teclado.
Es un solo `.exe` sin instalación; usa .NET Framework 4.8, ya incluido en Windows 10 y 11.

## Modos

- **Pantalla:** dibuja sobre cualquier programa. Con el puntero (`Esc` o `Ctrl+Alt+D`) sigues usando el equipo sin borrar el dibujo.
- **Hojas:** varias hojas con zoom y desplazamiento, sobre una superficie de 16000 × 12000 puntos.
- **Escribir:** una página de texto lista para teclear, con formato.

## Funciones

| Área | Incluye |
|---|---|
| Lápices | Bolígrafo, lápiz, rotulador, pincel, caligrafía, neón, discontinuo, punteado, aerosol, arcoíris y tinta que se desvanece |
| Otras herramientas de dibujo | Resaltador, borrador parcial y de trazo completo, selección para mover y redimensionar, convertir dibujos en formas |
| Color | 10 colores rápidos, 48 colores ampliados, selector personalizado, cuentagotas y opacidad |
| Formas | Línea, flecha, flecha doble, rectángulo, rectángulo redondeado, elipse, triángulos, rombo, pentágono, hexágono, estrella y ejes; con relleno opcional |
| Texto | Cuadros de texto, notas adhesivas, 15 tipos de letra, tamaño, color, negrita, cursiva, subrayado, tachado, resaltado, alineación, listas y sangría |
| Hojas | 9 fondos (blanca, cuadriculada, rayada, puntos, negra, verde, milimetrada, pentagrama, isométrica), duplicar, reordenar y plantillas (Kanban, DAFO, Cornell, plan semanal) |
| Imágenes | Insertar archivos, pegar con `Ctrl+V` y capturar la pantalla en una hoja nueva |
| Geometría | Regla con ajuste del lápiz a su borde y transportador |
| Presentar | Puntero láser, foco y lupa |
| Guardar | Cuaderno `.pizarra`, PNG, JPEG, PDF con todas las hojas y autoguardado cada minuto |

Pulsa el botón **?** de la barra para ver todos los atajos de teclado.

## Compilar

No hace falta instalar nada: el compilador viene con Windows.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

## Pruebas

```powershell
powershell -ExecutionPolicy Bypass -File tests\run.ps1
```

Compila una versión de prueba, la ejecuta con las ventanas invisibles y guarda el resultado en `tests\salida\resultado.txt`.
Las pruebas llaman directamente al código: no sustituyen a probar con un ratón o lápiz real.

## No incluido

Colaboración en tiempo real, nube, importar PDF, grabar vídeo y reconocer escritura a mano.
