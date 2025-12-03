import numpy as np
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D
import json
import sys

def load_pattern_from_json(filename):
    """Load pattern from a JSON file."""
    try:
        with open(filename, 'r') as f:
            data = json.load(f)
            # Handle both {"pattern": [...]} and direct [...]
            if isinstance(data, dict) and 'pattern' in data:
                return data['pattern']
            elif isinstance(data, list):
                return data
            else:
                raise ValueError("JSON must contain either a 'pattern' key or be a direct array")
    except FileNotFoundError:
        print(f"Error: File '{filename}' not found!")
        sys.exit(1)
    except json.JSONDecodeError as e:
        print(f"Error: Invalid JSON in file '{filename}': {e}")
        sys.exit(1)

def pattern_to_voxels(pattern):
    """Convert pattern array to 3D voxel grid matching C# ParsePattern logic.
    
    Pattern structure: Pattern[yLayer][z][x]
    - yLayer: Y coordinate (layer/height)
    - z: Z coordinate (row in each layer)
    - x: X coordinate (column in each row)
    
    Output: voxel_grid[x, y, z] = bool
    """
    if not pattern or not pattern[0]:
        return np.zeros((16, 16, 16), dtype=bool)
    
    # Determine grid size from pattern
    grid_size = 16  # Default
    if pattern and pattern[0]:
        grid_size = max(len(pattern[0]), len(pattern[0][0]) if pattern[0] else 16)
    
    # Initialize 3D grid: Voxels[x, y, z]
    voxel_grid = np.zeros((grid_size, grid_size, grid_size), dtype=bool)
    
    # Check if pattern should be extruded (single layer)
    extrude = (len(pattern) == 1)
    layers_to_process = 1 if extrude else min(len(pattern), grid_size)
    
    for y_layer in range(layers_to_process):
        rows = pattern[y_layer]
        
        for z in range(min(len(rows), grid_size)):
            row = rows[z]
            
            for x in range(min(len(row), grid_size)):
                char = row[x]
                is_solid = (char == '#')
                
                if extrude:
                    # Fill entire Y column for this x,z coordinate
                    for y in range(grid_size):
                        voxel_grid[x, y, z] = is_solid
                else:
                    # Single voxel at this coordinate
                    voxel_grid[x, y_layer, z] = is_solid
    
    return voxel_grid

def visualize_voxels(pattern):
    """Create 3D visualization of voxel pattern."""
    voxel_grid = pattern_to_voxels(pattern)
    
    # Check if there are any voxels to display
    if not voxel_grid.any():
        print("No voxels to display!")
        return
    
    # Create figure
    fig = plt.figure(figsize=(12, 10))
    ax = fig.add_subplot(111, projection='3d')
    
    # Plot voxels
    ax.voxels(voxel_grid, facecolors='steelblue', edgecolors='black', alpha=0.8, linewidth=0.5)
    
    # Set labels and title
    ax.set_xlabel('X')
    ax.set_ylabel('Y (Height)')
    ax.set_zlabel('Z')
    ax.set_title('3D Voxel Visualization')
    
    # Set limits based on actual grid size
    x_size, y_size, z_size = voxel_grid.shape
    ax.set_xlim([0, x_size])
    ax.set_ylim([0, y_size])
    ax.set_zlim([0, z_size])
    
    # Improve viewing angle
    ax.view_init(elev=20, azim=45)
    
    plt.tight_layout()
    plt.show()

def print_pattern_info(pattern):
    """Print detailed information about the pattern."""
    voxel_grid = pattern_to_voxels(pattern)
    total_voxels = np.sum(voxel_grid)
    
    print("\n=== Pattern Information ===")
    print(f"Pattern layers: {len(pattern)}")
    print(f"Grid dimensions: {voxel_grid.shape[0]}x{voxel_grid.shape[1]}x{voxel_grid.shape[2]} (X x Y x Z)")
    print(f"Total filled voxels: {total_voxels}")
    print(f"Extrusion mode: {'Yes' if len(pattern) == 1 else 'No'}")
    
    # Print layer-by-layer breakdown
    if len(pattern) > 1:
        print(f"\nVoxels per Y-layer:")
        for y in range(voxel_grid.shape[1]):
            count = np.sum(voxel_grid[:, y, :])
            if count > 0:
                print(f"  Y={y}: {count} voxels")

# Main execution
if len(sys.argv) > 1:
    json_filename = sys.argv[1]
    print(f"Loading pattern from: {json_filename}")
    pattern = load_pattern_from_json(json_filename)
    
    # Print info
    print_pattern_info(pattern)
    
    # Visualize
    visualize_voxels(pattern)
else:
    print("Usage: python visualiser.py <pattern.json>")
    print("Example: python visualiser.py pattern.json")
    sys.exit(1)