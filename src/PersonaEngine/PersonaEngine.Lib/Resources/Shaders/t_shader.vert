// Attributes
attribute vec3 a_position;
attribute vec4 a_color;
attribute vec2 a_texCoords0;

// Uniforms
uniform mat4 MatrixTransform;

// Varyings
varying vec4 v_color;
varying vec2 v_texCoords;

void main()
{
	v_color = a_color;
	v_texCoords = a_texCoords0;
	gl_Position = MatrixTransform * vec4(a_position, 1.0);
}