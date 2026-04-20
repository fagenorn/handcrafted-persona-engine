#ifdef GL_ES
	#define LOWP lowp
	precision mediump float;
#else
	#define LOWP
#endif


// Uniforms
uniform sampler2D TextureSampler;

// Varyings
varying vec4 v_color;
varying vec2 v_texCoords;

void main()
{
	gl_FragColor = v_color * texture2D(TextureSampler, v_texCoords);
}